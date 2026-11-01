using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CVManagement.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;

namespace CVManagement.Tests.Integration;

/// Owns the SQL Server container for the whole test run and the API factory wired to it.
public sealed class SqlApiFixture : IAsyncLifetime
{
    private const string ConnectionStringKey = "ConnectionStrings__CVManagement";
    private const string EncryptionKey = "CvEncryption__Key";
    private const string SigningKey = "CvAuth__SigningKey";

    /// Strong enough for the password rules; constant so every test can log the admin in.
    public const string AdminPassword = "integration-admin-password";

    private MsSqlContainer? _sql;

    private string? _adminToken;

    public ApiFactory Factory { get; private set; } = null!;

    /// The key material the API boots with, so a test can plant a value in a chosen stored format.
    public byte[] EncryptionKeyBytes { get; private set; } = [];

    /// The HMAC signing key the API boots with, so a test can forge tokens and prove rejection.
    public byte[] AuthSigningKeyBytes { get; private set; } = [];

    /// Raw ADO connection string for at-rest assertions that bypass the app.
    public string ConnectionString => _sql is null
        ? throw new InvalidOperationException("SQL Server container did not start; integration tests require Docker.")
        : _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        // No Docker fallback: StartAsync throws when the engine is missing, failing the tests loudly.
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _sql.StartAsync();

        // Program.cs reads config at startup, before factory hooks apply, so inject via environment.
        Environment.SetEnvironmentVariable(ConnectionStringKey, _sql.GetConnectionString());
        EncryptionKeyBytes = RandomNumberGenerator.GetBytes(32);
        Environment.SetEnvironmentVariable(EncryptionKey, Convert.ToBase64String(EncryptionKeyBytes));
        AuthSigningKeyBytes = RandomNumberGenerator.GetBytes(64);
        Environment.SetEnvironmentVariable(SigningKey, Convert.ToBase64String(AuthSigningKeyBytes));

        Factory = new ApiFactory();

        // The first and only bootstrap: creates the admin account every authenticated test uses.
        var bootstrapClient = Factory.CreateClient();
        var bootstrapResponse = await bootstrapClient.PostAsJsonAsync("/api/auth/bootstrap",
            new { username = "admin", password = AdminPassword });

        if (!bootstrapResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Bootstrap failed with {bootstrapResponse.StatusCode}; the authenticated tests cannot proceed.");
        }
    }

    public async Task DisposeAsync()
    {
        Factory.Dispose();

        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }

        Environment.SetEnvironmentVariable(ConnectionStringKey, null);
        Environment.SetEnvironmentVariable(EncryptionKey, null);
        Environment.SetEnvironmentVariable(SigningKey, null);
    }

    /// A client authenticated as the bootstrapped admin; the standard client for every test.
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var token = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// A client authenticated as an arbitrary non-admin account, for role-boundary tests.
    public async Task<HttpClient> CreateUserClientAsync(string username, string password)
    {
        var response = await Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username, password });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login for '{username}' failed with {response.StatusCode}.");
        }

        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> GetAdminTokenAsync()
    {
        if (_adminToken is null)
        {
            var response = await Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = AdminPassword });

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Admin login failed with {response.StatusCode}.");
            }

            _adminToken = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
        }

        return _adminToken;
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFixture>
{
    public const string Name = "SQL API";
}
