using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CVManagement.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CVManagement.Tests.Integration;

/// Production-posture pins over what is actually implemented: the API must not expose debug
/// surfaces in production, must answer faults without internals, and must fail startup loudly
/// without ever echoing the secrets it was denied.
///
/// Not covered because not implemented, deliberately deferred: rate limiting, HTTPS redirection,
/// HSTS, and security headers. They belong to the deployment decision, not to this codebase yet.
[Collection(SqlApiCollection.Name)]
public sealed class ProductionReadinessTests
{
    private readonly SqlApiFixture _fixture;

    public ProductionReadinessTests(SqlApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ProductionMode_ServesNoSwaggerAndLeaksNoInternalsInFailures()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var token = await LoginAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // No debug surfaces in production.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);

        // A candidate whose stored Email no longer decrypts is the fault the sanitized 500 exists for.
        var created = await client.PostAsJsonAsync("/api/candidates",
            new CandidateRequest { FirstName = "Broken", LastName = "Record", Email = "broken@example.com" });
        var candidate = (await created.Content.ReadFromJsonAsync<CandidateDto>())!;

        try
        {
            await ExecuteAsync("UPDATE Candidates SET Email = @value WHERE Id = @id", candidate.Id, "tampered-not-a-ciphertext");

            var faulted = await client.GetAsync($"/api/candidates/{candidate.Id}");

            Assert.Equal(HttpStatusCode.InternalServerError, faulted.StatusCode);

            var body = await faulted.Content.ReadAsStringAsync();

            // The body is a ProblemDetails shell; it must carry none of the internals.
            Assert.Contains("500", body);
            Assert.DoesNotContain("CryptographicException", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UnreadableStoredData", body);
            Assert.DoesNotContain("AesGcm", body);
            Assert.DoesNotContain("at CVManagement", body);
        }
        finally
        {
            // Leave the shared database clean: a row that no longer decrypts would fault every
            // later list probe, so it is removed by key.
            await ExecuteAsync("DELETE FROM Candidates WHERE Id = @id", candidate.Id);
        }
    }

    [Fact]
    public void Startup_WithoutAConnectionString_FailsLoudly()
    {
        AssertStartupFailsAfterRemoving("ConnectionStrings__CVManagement", "Connection string 'CVManagement'");
    }

    [Fact]
    public void Startup_WithAnUndersizedSigningKey_FailsWithoutEchoingTheKey()
    {
        var previous = Environment.GetEnvironmentVariable("CvAuth__SigningKey");
        var shortKey = Convert.ToBase64String(new byte[8]);

        Environment.SetEnvironmentVariable("CvAuth__SigningKey", shortKey);

        try
        {
            var failure = AttemptStartup();

            Assert.NotNull(failure);
            Assert.Contains("at least 32 bytes", failure!.ToString());
            Assert.DoesNotContain(shortKey, failure.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CvAuth__SigningKey", previous);
        }
    }

    [Fact]
    public void Startup_WithAMalformedConnectionString_FailsFastWithoutLeakingThePassword()
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__CVManagement");

        // The malformed keyword fails connection construction immediately, before the startup
        // retry loop, which is what makes this test fast; the retry path against a silent host
        // would take ten attempts by design and is deliberately not pinned here.
        Environment.SetEnvironmentVariable("ConnectionStrings__CVManagement",
            "Server=localhost,1433;Database=x;User Id=sa;Password=SuperSecret123;NotAKeyword=True");

        try
        {
            var failure = AttemptStartup();

            Assert.NotNull(failure);
            Assert.DoesNotContain("SuperSecret123", failure!.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__CVManagement", previous);
        }
    }

    [Fact]
    public void Startup_WithTheSameKeyForBothPurposes_Fails()
    {
        var previousSigningKey = Environment.GetEnvironmentVariable("CvAuth__SigningKey");
        var encryptionKey = Environment.GetEnvironmentVariable("CvEncryption__Key");

        Environment.SetEnvironmentVariable("CvAuth__SigningKey", encryptionKey);

        try
        {
            var failure = AttemptStartup();

            Assert.NotNull(failure);
            Assert.Contains("must differ", failure!.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CvAuth__SigningKey", previousSigningKey);
        }
    }

    private static Exception? AttemptStartup()
    {
        try
        {
            using var factory = new ApiFactory();
            factory.CreateClient();
        }
        catch (Exception ex)
        {
            return ex;
        }

        return null;
    }

    private static void AssertStartupFailsAfterRemoving(string variable, string expectedInMessage)
    {
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, null);

        try
        {
            var failure = AttemptStartup();

            Assert.NotNull(failure);
            Assert.Contains(expectedInMessage, failure!.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private static async Task<string> LoginAsync(WebApplicationFactory<Program> factory)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = SqlApiFixture.AdminPassword });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Admin login failed with {response.StatusCode}.");
        }

        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
    }

    private async Task ExecuteAsync(string sql, int id, string? value = null)
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);

        if (value is not null)
        {
            command.Parameters.AddWithValue("@value", value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
