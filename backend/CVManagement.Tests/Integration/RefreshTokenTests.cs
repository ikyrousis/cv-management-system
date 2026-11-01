using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using CVManagement.DTOs;
using CVManagement.Validation;
using Xunit;

namespace CVManagement.Tests.Integration;

/// The refresh credential lifecycle against the running API with a real SQL Server: rotation,
/// replay containment, logout, deactivation, at-rest storage and the consume race.
[Collection(SqlApiCollection.Name)]
public sealed class RefreshTokenTests
{
    private readonly SqlApiFixture _fixture;

    public RefreshTokenTests(SqlApiFixture fixture) => _fixture = fixture;

    /// A fresh session for a dedicated account, so no other test's tokens are disturbed.
    private async Task<TokenResponse> CreateSessionAsync(string username)
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, password = "session-password-1", role = AuthLimits.UserRole });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username, password = "session-password-1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken });

    private async Task<HttpResponseMessage> LogoutAsync(TokenResponse session)
    {
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        return await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.RefreshToken });
    }

    [Fact]
    public async Task Login_ReturnsAUsableRefreshToken()
    {
        var session = await CreateSessionAsync($"session-{Guid.NewGuid():N}");

        Assert.NotEqual(string.Empty, session.RefreshToken);
        Assert.True(session.RefreshExpiresAtUtc > DateTime.UtcNow, "The refresh credential must arrive unexpired.");

        var refreshed = await RefreshAsync(session.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var next = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(string.Empty, next.Token);
        Assert.NotEqual(string.Empty, next.RefreshToken);
        Assert.False(string.Equals(session.RefreshToken, next.RefreshToken, StringComparison.Ordinal),
            "Rotation must never hand back the same credential.");

        // The new access token unlocks the API: the refresh produced a real session.
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/candidates")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesAndReplayKillsTheWholeChain()
    {
        var session = await CreateSessionAsync($"rotation-{Guid.NewGuid():N}");

        var first = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var successor = (await first.Content.ReadFromJsonAsync<TokenResponse>())!;

        // The used credential is dead on its own.
        var replay = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // Replay of a revoked token is theft-shaped: the successor dies with it, so the thief and
        // the legitimate holder are locked out together and must log in again.
        var successorAttempt = await RefreshAsync(successor.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, successorAttempt.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownOrMalformedCredentials_AreRefused()
    {
        var unknown = await RefreshAsync("definitely-not-a-issued-token");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);

        var empty = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_IsRefused_AndCausesNoCollateralDamage()
    {
        // A planted row, written through raw ADO with an expiry in the past: the at-rest shape a
        // credential would have after sitting unused past its window.
        const string raw = "planted-expired-refresh-token";
        await ExecuteAsync(
            "INSERT INTO RefreshTokens (UserId, TokenHash, CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc) " +
            "SELECT Id, @hash, DATEADD(day, -10, GETUTCDATE()), DATEADD(day, -3, GETUTCDATE()), NULL " +
            "FROM Users WHERE Username = 'admin'",
            ("@hash", StorageHash(raw)));

        var response = await RefreshAsync(raw);

        // Expired is refused, but expiry is abandonment, not theft: no containment, so the
        // account's live credentials keep working afterwards.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = SqlApiFixture.AdminPassword });
        var session = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        var live = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var session = await CreateSessionAsync($"logout-{Guid.NewGuid():N}");

        var logout = await LogoutAsync(session);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_UnknownOrUsedToken_AnswersSuccessWithNoOracle()
    {
        var session = await CreateSessionAsync($"logout-oracle-{Guid.NewGuid():N}");

        // A bogus credential: still 204, because the caller is authenticated and the answer must
        // not reveal which refresh tokens exist.
        var bogus = await LogoutAsync(session with { RefreshToken = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.NoContent, bogus.StatusCode);

        // The real token survives a logout that revoked nothing else.
        var refresh = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        // Logging out twice is harmless, and the token is gone regardless of how often it is asked.
        var first = await LogoutAsync(session);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var second = await LogoutAsync(session);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var afterLogout = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Logout_RequiresAuthentication()
    {
        var anonymous = _fixture.Factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = "anything" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeactivatedAccount_CannotRefresh_EvenWithAnUnexpiredToken()
    {
        var session = await CreateSessionAsync($"deactivated-{Guid.NewGuid():N}");

        var admin = await _fixture.CreateAuthenticatedClientAsync();
        var users = await admin.GetFromJsonAsync<List<UserDto>>("/api/users");
        var account = users!.Single(u => u.Username.StartsWith("deactivated-"));

        await admin.PostAsync($"/api/users/{account.Id}/deactivate", null);

        var refresh = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // The access token already issued keeps working until its expiry: the stateless pinned
        // guarantee, which the deactivation test in AuthenticationTests holds separately.
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/candidates")).StatusCode);
    }

    [Fact]
    public async Task StoredCredentials_AreDigests_TheRawTokenNeverReachesTheTable()
    {
        var session = await CreateSessionAsync($"atrest-{Guid.NewGuid():N}");

        var storedHashes = await ReadColumnAsync("SELECT TokenHash FROM RefreshTokens");
        Assert.NotEmpty(storedHashes);

        // No row stores the raw value, in any column.
        var fullRows = await ReadColumnAsync("SELECT CAST(TokenHash AS VARCHAR(MAX)) FROM RefreshTokens");
        Assert.All(fullRows, row => Assert.False(row.Contains(session.RefreshToken, StringComparison.Ordinal),
            $"The raw refresh token must never appear in storage, but a row contains it."));

        // The stored digest is exactly SHA-256 of the raw token, computed here independently.
        Assert.Contains(StorageHash(session.RefreshToken), storedHashes);
    }

    [Fact]
    public async Task ConcurrentRefreshes_WithOneToken_ProduceExactlyOneSession()
    {
        var session = await CreateSessionAsync($"race-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(Enumerable.Range(1, 8)
            .Select(_ => RefreshAsync(session.RefreshToken)));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(1, succeeded);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));

        // The one winner's credential is dead anyway: the seven losers marked the token used and
        // triggered containment, so the whole chain must refuse afterwards.
        var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.OK)
            .Content.ReadFromJsonAsync<TokenResponse>())!;
        var winnerReplay = await RefreshAsync(winner.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, winnerReplay.StatusCode);
    }

    /// SHA-256, base64: the storage digest, computed independently of the API.
    private static string StorageHash(string rawToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadColumnAsync(string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
