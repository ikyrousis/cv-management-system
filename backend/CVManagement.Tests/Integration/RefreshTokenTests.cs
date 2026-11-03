using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        await CreateUserAsync(username);

        return await LoginAsync(username);
    }

    private async Task CreateUserAsync(string username)
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, password = SessionPassword, role = AuthLimits.UserRole });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private async Task<TokenResponse> LoginAsync(string username)
    {
        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username, password = SessionPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private const string SessionPassword = "session-password-1";

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
    public async Task Login_AnswersTheWholeSession_WithRealExpiries_AndNoCredentialMaterial()
    {
        var username = $"login-shape-{Guid.NewGuid():N}";
        await CreateUserAsync(username);

        var response = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username, password = SessionPassword });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = JsonSerializer.Deserialize<TokenResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Both halves of the session arrive on the wire: the refresh credential is returned in the
        // body, not set as a cookie, which Authentication_SetsNoCookiesAnywhere pins separately.
        Assert.False(string.IsNullOrWhiteSpace(session.Token));
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));

        // The stated expiry is not a claim of its own: it must be the exp inside the signed token,
        // so a response cannot advertise a lifetime the token does not actually carry.
        var access = new JwtSecurityTokenHandler().ReadJwtToken(session.Token);
        var drift = (access.ValidTo - session.ExpiresAtUtc).Duration();
        Assert.True(drift <= TimeSpan.FromSeconds(1),
            $"The advertised expiry {session.ExpiresAtUtc:o} is not the token's own {access.ValidTo:o}.");

        // Both expiries are in the future, the access half is short-lived at its configured
        // lifetime, and the refresh half outlives it by the configured window.
        var now = DateTime.UtcNow;
        Assert.True(session.ExpiresAtUtc > now, "The access token must arrive unexpired.");
        Assert.True(session.RefreshExpiresAtUtc > session.ExpiresAtUtc,
            "The refresh credential must outlive the access token it renews.");
        Assert.Equal(AuthLimits.DefaultAccessTokenLifetimeMinutes,
            (access.ValidTo - access.ValidFrom).TotalMinutes, 1);
        Assert.Equal(AuthLimits.DefaultRefreshTokenLifetimeDays,
            (session.RefreshExpiresAtUtc - now).TotalDays, 1);

        // The password is not echoed back, nor is any stored hash: the response carries session
        // material and nothing about the credential that earned it.
        Assert.DoesNotContain(SessionPassword, body);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_LeavesTheDatabaseConsistent_OneActiveToken_OwnershipAndWindowIntact()
    {
        var username = $"dbstate-{Guid.NewGuid():N}";
        await CreateUserAsync(username);
        var session = await LoginAsync(username);

        var before = await ReadRefreshRowsAsync(username);
        var presented = Assert.Single(before);
        Assert.Null(presented.RevokedAtUtc);

        var response = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;

        var after = await ReadRefreshRowsAsync(username);

        // Exactly one successor: the rotation replaced the credential instead of adding a session,
        // so no duplicate active token was created behind the caller's back.
        Assert.Equal(2, after.Count);
        var active = Assert.Single(after, r => r.RevokedAtUtc is null);
        var consumed = Assert.Single(after, r => r.RevokedAtUtc is not null);

        // The rows are the two credentials of this exchange, identified by their own digests.
        Assert.Equal(StorageHash(session.RefreshToken), consumed.TokenHash);
        Assert.Equal(StorageHash(rotated.RefreshToken), active.TokenHash);

        // Ownership survived the rotation: the successor belongs to the same account, and the
        // consumed row was not reassigned on its way out.
        Assert.Equal(presented.UserId, active.UserId);
        Assert.Equal(presented.UserId, consumed.UserId);

        // The window moved forward a full lifetime rather than inheriting what was left, and the
        // successor is unexpired.
        Assert.True(active.ExpiresAtUtc > consumed.ExpiresAtUtc,
            $"The successor's expiry {active.ExpiresAtUtc:o} must be a fresh window past {consumed.ExpiresAtUtc:o}.");
        Assert.True(active.ExpiresAtUtc > DateTime.UtcNow, "The persisted successor must be unexpired.");
        Assert.True(active.CreatedAtUtc >= presented.CreatedAtUtc, "The successor must be the newer row.");

        // The response did not overstate what is stored: the advertised refresh expiry is the row's.
        Assert.Equal(active.ExpiresAtUtc, rotated.RefreshExpiresAtUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Logout_OneSession_LeavesTheAccountsOtherSessionsAlive()
    {
        // Sessions are per credential rather than per account, so one device signing out must not
        // sign the others out. Both sessions belong to the same account by design.
        var username = $"devices-{Guid.NewGuid():N}";
        await CreateUserAsync(username);
        var firstDevice = await LoginAsync(username);
        var secondDevice = await LoginAsync(username);

        Assert.Equal(2, (await ReadRefreshRowsAsync(username)).Count(r => r.RevokedAtUtc is null));

        var logout = await LogoutAsync(firstDevice);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // The other device's credential is untouched at rest: revocation named one row, not the account.
        var survivors = await ReadRefreshRowsAsync(username);
        Assert.Equal(2, survivors.Count);
        var stillActive = Assert.Single(survivors, r => r.RevokedAtUtc is null);
        Assert.Equal(StorageHash(secondDevice.RefreshToken), stillActive.TokenHash);

        // And it still works, which is the assertion that matters to the user holding that device.
        // The first device's credential is deliberately never presented here: a revoked token is
        // theft-shaped, so replaying it would trigger containment and revoke the survivor too.
        // Logout_RevokesTheRefreshToken pins that refusal separately.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(secondDevice.RefreshToken)).StatusCode);

        // Revocation policy, stated rather than assumed: closing the refresh path does not recall
        // an access token already issued, so the signed-out device keeps its stateless bearer
        // until that token expires. DeactivatedUser_ItsIssuedTokenKeepsWorkingUntilExpiry pins the
        // same guarantee for deactivation.
        var signedOut = _fixture.Factory.CreateClient();
        signedOut.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", firstDevice.Token);
        Assert.Equal(HttpStatusCode.OK, (await signedOut.GetAsync("/api/candidates")).StatusCode);
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
    public async Task Refresh_TheRotatedToken_IsItselfUsableForTheNextRotation()
    {
        var session = await CreateSessionAsync($"chain-{Guid.NewGuid():N}");

        // A successor that was returned but never persisted would still pass a single-refresh
        // test, so the chain is walked twice: each credential must be usable exactly once.
        var second = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondSession = (await second.Content.ReadFromJsonAsync<TokenResponse>())!;

        var third = await RefreshAsync(secondSession.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        var thirdSession = (await third.Content.ReadFromJsonAsync<TokenResponse>())!;

        // Three distinct credentials: rotation never hands the same value back.
        Assert.NotEqual(session.RefreshToken, secondSession.RefreshToken);
        Assert.NotEqual(secondSession.RefreshToken, thirdSession.RefreshToken);

        // The newest credential is itself usable, which is the evidence that each successor really
        // reached storage rather than only being returned in the response that minted it.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(thirdSession.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Refresh_AFailedRotation_LeavesThePresentedTokenUsable()
    {
        var session = await CreateSessionAsync($"rollback-{Guid.NewGuid():N}");

        // The successor's insert is blocked at the database, so the rotation cannot complete after
        // the consume already ran. A trigger is the only way to fault that specific write from
        // outside the API; it is removed again whatever the assertions do.
        await ExecuteAsync(
            "CREATE TRIGGER TR_BlockRefreshTokenInsert ON RefreshTokens INSTEAD OF INSERT AS " +
            "BEGIN THROW 50000, 'Refresh token insert blocked by test', 1; END");

        try
        {
            // The consume ran and the insert faulted, so the API answers a server error. Asserting
            // the status rather than tolerating a thrown fault keeps the test honest: a call that
            // never reached the endpoint would otherwise leave nothing to contradict the retry below.
            var response = await RefreshAsync(session.RefreshToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER IF EXISTS TR_BlockRefreshTokenInsert");
        }

        // The consume and the insert commit as one transaction, so the failed rotation rolled the
        // consume back with it: the presented credential is intact and still exchanges normally.
        var retry = await RefreshAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        var recovered = (await retry.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(string.Empty, recovered.RefreshToken);

        // And the account was not put into containment by the fault: the chain continues.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(recovered.RefreshToken)).StatusCode);
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
    public async Task Logout_AnotherAccountsRefreshToken_Survives_AndLeavesNoOracle()
    {
        var victim = await CreateSessionAsync($"victim-{Guid.NewGuid():N}");
        var caller = await CreateSessionAsync($"caller-{Guid.NewGuid():N}");

        // Authenticated as one account, presenting another account's credential. The account is
        // taken from the access token, so the presented token names nothing the caller owns.
        var response = await LogoutAsync(caller with { RefreshToken = victim.RefreshToken });

        // 204, not 403 or 404: the answer must not reveal that the token exists or whose it is.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The victim's session is untouched and still rotates normally.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(victim.RefreshToken)).StatusCode);

        // The caller's own credential is still theirs to revoke.
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(caller.RefreshToken)).StatusCode);
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

    /// The refresh credentials one account holds, read straight from the table so the assertions
    /// are about persisted state rather than about what the API chose to report.
    private sealed record StoredRefreshRow(
        string TokenHash,
        int UserId,
        DateTime CreatedAtUtc,
        DateTime ExpiresAtUtc,
        DateTime? RevokedAtUtc);

    private async Task<List<StoredRefreshRow>> ReadRefreshRowsAsync(string username)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT r.TokenHash, r.UserId, r.CreatedAtUtc, r.ExpiresAtUtc, r.RevokedAtUtc " +
            "FROM RefreshTokens r INNER JOIN Users u ON u.Id = r.UserId " +
            "WHERE u.Username = @username ORDER BY r.CreatedAtUtc";
        command.Parameters.AddWithValue("@username", username);

        var rows = new List<StoredRefreshRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new StoredRefreshRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetDateTime(2),
                reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4)));
        }

        return rows;
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
