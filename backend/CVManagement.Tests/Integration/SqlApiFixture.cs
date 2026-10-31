using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;

namespace CVManagement.Tests.Integration;

/// Owns the SQL Server container for the whole test run and the API factory wired to it.
public sealed class SqlApiFixture : IAsyncLifetime
{
    private const string ConnectionStringKey = "ConnectionStrings__CVManagement";
    private const string EncryptionKey = "CvEncryption__Key";

    private MsSqlContainer? _sql;

    public ApiFactory Factory { get; private set; } = null!;

    /// The key material the API boots with, so a test can plant a value in a chosen stored format.
    public byte[] EncryptionKeyBytes { get; private set; } = [];

    /// Raw ADO connection string for at-rest assertions that bypass the app.
    public string ConnectionString => _sql is null
        ? throw new InvalidOperationException("SQL Server container did not start; integration tests require Docker.")
        : _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        // No Docker fallback: StartAsync throws when the engine is missing, failing the tests loudly.
        _sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _sql.StartAsync();

        // Program.cs reads config at startup, before factory hooks apply, so inject via environment.
        Environment.SetEnvironmentVariable(ConnectionStringKey, _sql.GetConnectionString());
        EncryptionKeyBytes = RandomNumberGenerator.GetBytes(32);
        Environment.SetEnvironmentVariable(EncryptionKey, Convert.ToBase64String(EncryptionKeyBytes));

        Factory = new ApiFactory();
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
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFixture>
{
    public const string Name = "SQL API";
}
