using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CVManagement.DTOs;
using CVManagement.Validation;
using Xunit;

namespace CVManagement.Tests.Integration;

/// The admin password reset against the running API with a real SQL Server: authorization, the
/// contract, hashing at rest, session revocation and concurrent resets.
[Collection(SqlApiCollection.Name)]
public sealed class PasswordResetTests
{
    private readonly SqlApiFixture _fixture;

    public PasswordResetTests(SqlApiFixture fixture) => _fixture = fixture;

    /// A dedicated account per test, so no other test's state is disturbed.
    private async Task<UserDto> CreateUserAsync(string username, string password)
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, password, role = AuthLimits.UserRole });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private static (string Username, string Password) Credentials(string tag)
        => ($"reset-{tag}-{Guid.NewGuid():N}", "initial-password-1");

    [Fact]
    public async Task ResetPassword_RequiresTheAdminRole()
    {
        var (username, password) = Credentials("authz");
        var target = await CreateUserAsync(username, password);

        var anonymous = _fixture.Factory.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync($"/api/users/{target.Id}/password",
            new { newPassword = "whatever-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        // A non-admin account, created and logged in: it can use the API but never this surface.
        var plainUsername = $"plain-{Guid.NewGuid():N}";
        await CreateUserAsync(plainUsername, "plain-user-password");
        var userClient = await _fixture.CreateUserClientAsync(plainUsername, "plain-user-password");
        var userResponse = await userClient.PostAsJsonAsync($"/api/users/{target.Id}/password",
            new { newPassword = "whatever-password" });
        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_AnswersNotFound()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users/999999/password",
            new { newPassword = "whatever-password" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_MalformedRequests_AreRejectedByTheContract()
    {
        var (username, password) = Credentials("contract");
        var target = await CreateUserAsync(username, password);
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var missing = await admin.PostAsJsonAsync($"/api/users/{target.Id}/password", new { });
        var shortPassword = await admin.PostAsJsonAsync($"/api/users/{target.Id}/password",
            new { newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ReplacesTheCredential_Answering204WithNoBody()
    {
        var (username, password) = Credentials("replace");
        var target = await CreateUserAsync(username, password);
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/users/{target.Id}/password",
            new { newPassword = "replacement-password" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, (await response.Content.ReadAsStringAsync()).Trim());

        // The old credential is dead, the new one works, and the answer carried no login material.
        var client = _fixture.Factory.CreateClient();
        var oldLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = password });
        var newLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = "replacement-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_StoresAnEncryptedHash_NeverThePlaintext()
    {
        var (username, password) = Credentials("atrest");
        var target = await CreateUserAsync(username, password);
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/users/{target.Id}/password",
            new { newPassword = "replacement-password" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // What is at rest, read through raw ADO, bypassing the app entirely.
        var stored = await ReadStoredHashAsync(username);

        Assert.NotNull(stored);
        Assert.NotEqual("replacement-password", stored);
        Assert.DoesNotContain("replacement-password", stored);
        Assert.DoesNotContain("initial-password-1", stored);

        // The stored value is usable ciphertext: the account logs in through the decrypt-verify
        // path, which proves the hash was written through the column's converter.
        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username, password = "replacement-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_RevokesEveryRefreshTokenOfTheAccount()
    {
        var (username, password) = Credentials("revoke");
        var target = await CreateUserAsync(username, password);

        var firstLogin = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username, password });
        var firstSession = (await firstLogin.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);

        var admin = await _fixture.CreateAuthenticatedClientAsync();
        var reset = await admin.PostAsJsonAsync($"/api/users/{target.Id}/password",
            new { newPassword = "replacement-password" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // The pre-reset refresh credential is dead: the reset killed every session's refresh path.
        var client = _fixture.Factory.CreateClient();
        var refreshAttempt = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = firstSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAttempt.StatusCode);

        // The new password opens a fresh session whose refresh credential works normally.
        var newLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = "replacement-password" });
        var newSession = (await newLogin.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        var newRefresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = newSession.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, newRefresh.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ConcurrentResets_ProduceExactlyOneConsistentCredential()
    {
        var (username, password) = Credentials("race");
        var target = await CreateUserAsync(username, password);
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var passwords = Enumerable.Range(1, 5).Select(i => $"racing-password-{i}").ToList();

        // Five concurrent resets of the same account: the transactions serialize on the row,
        // so the final stored hash is exactly one of the five, never a mix.
        var responses = await Task.WhenAll(passwords.Select(p =>
            admin.PostAsJsonAsync($"/api/users/{target.Id}/password", new { newPassword = p })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        var client = _fixture.Factory.CreateClient();
        var logins = await Task.WhenAll(passwords.Select(p =>
            client.PostAsJsonAsync("/api/auth/login", new { username, password = p })));

        var working = logins.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(1, working);
        Assert.All(logins.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    private async Task<string?> ReadStoredHashAsync(string username)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordHash FROM Users WHERE Username = @username";
        command.Parameters.AddWithValue("@username", username);

        return await command.ExecuteScalarAsync() as string;
    }
}
