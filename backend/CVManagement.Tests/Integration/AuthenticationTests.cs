using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CVManagement.DTOs;
using CVManagement.Validation;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CVManagement.Tests.Integration;

/// Every security claim about authentication, asserted against the running API. The fixture has
/// already bootstrapped the single admin account, so bootstrap closure is testable directly.
[Collection(SqlApiCollection.Name)]
public sealed class AuthenticationTests
{
    private readonly SqlApiFixture _fixture;

    public AuthenticationTests(SqlApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnonymousRequests_AreRejectedByEveryProtectedEndpoint()
    {
        var client = _fixture.Factory.CreateClient();

        var list = await client.GetAsync("/api/candidates");
        var create = await client.PostAsJsonAsync("/api/degrees", new { name = "Whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public void MissingSigningKey_FailsTheStartupLoudly()
    {
        // An API that boots without its signing key would serve every endpoint unauthenticated,
        // so startup must refuse instead. The exception may arrive wrapped; the message is the evidence.
        var previous = Environment.GetEnvironmentVariable("CvAuth__SigningKey");
        Environment.SetEnvironmentVariable("CvAuth__SigningKey", null);

        try
        {
            Exception? failure = null;
            try
            {
                using var factory = new ApiFactory();
                factory.CreateClient();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Assert.NotNull(failure);
            Assert.Contains("CvAuth", failure!.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CvAuth__SigningKey", previous);
        }
    }

    [Fact]
    public async Task Bootstrap_SecondAttempt_IsRefused()
    {
        var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/bootstrap",
            new { username = "intruder", password = "attacker-password-12" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUserAndWrongPassword_AnswerIdentically()
    {
        var client = _fixture.Factory.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "no-such-user", password = "some-password-123" });
        var wrong = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "not-the-real-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        // The response body is the oracle. ASP.NET Core stamps every client error with a random
        // per-request traceId, which says nothing about the credential, so it is stripped before
        // the comparison: everything left must be identical for the two failure causes.
        Assert.Equal(StripTraceId(await unknown.Content.ReadAsStringAsync()),
            StripTraceId(await wrong.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task TamperedPayload_CannotEscalateRoleOrSurvive()
    {
        // A genuine, correctly signed token carrying the User role.
        var genuine = CraftToken(_fixture.AuthSigningKeyBytes, role: AuthLimits.UserRole);

        var sanity = _fixture.Factory.CreateClient();
        sanity.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", genuine);
        Assert.Equal(HttpStatusCode.OK, (await sanity.GetAsync("/api/candidates")).StatusCode);

        // Rewrite the payload to claim the Admin role and keep the original signature: the API
        // must treat the token as tampered with, not as a promotion.
        var parts = genuine.Split('.');
        var payload = Base64UrlDecode(parts[1]);
        Assert.Contains(AuthLimits.UserRole, payload);

        var tampered = string.Join('.',
            parts[0],
            Base64UrlEncode(payload.Replace(AuthLimits.UserRole, AuthLimits.AdminRole)),
            parts[2]);

        var attacker = _fixture.Factory.CreateClient();
        attacker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/candidates")).StatusCode);
        // Authentication fails before authorization, so even the role-gated surface answers 401:
        // the API never got far enough to learn what the payload claimed.
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task TokenSignedWithADifferentKey_IsRejected()
    {
        var forged = CraftToken(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/candidates")).StatusCode);
    }

    [Fact]
    public async Task ExpiredAndNotYetValidTokens_AreRejected()
    {
        var expired = CraftToken(_fixture.AuthSigningKeyBytes,
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-10));
        var future = CraftToken(_fixture.AuthSigningKeyBytes,
            notBefore: DateTime.UtcNow.AddMinutes(10),
            expires: DateTime.UtcNow.AddMinutes(30));

        foreach (var token in new[] { expired, future })
        {
            var client = _fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/candidates")).StatusCode);
        }
    }

    [Fact]
    public async Task MalformedTokens_AreRejected()
    {
        foreach (var token in new[] { "not-a-jwt", "a.b", "....", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0" })
        {
            var client = _fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/candidates")).StatusCode);
        }
    }

    [Fact]
    public async Task TokensFromAnotherIssuerOrAudience_AreRejected()
    {
        var otherIssuer = CraftToken(_fixture.AuthSigningKeyBytes, issuer: "someone-else");
        var otherAudience = CraftToken(_fixture.AuthSigningKeyBytes, audience: "someone-else");

        foreach (var token in new[] { otherIssuer, otherAudience })
        {
            var client = _fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/candidates")).StatusCode);
        }
    }

    [Fact]
    public async Task CreateUser_PersistsTheUser_HashesThePasswordAndNeverLeaksIt()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        // Unknown fields must be inert: neither overridable nor privilege-granting.
        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            username = "persisted.user",
            password = "plaintext-to-be-hashed",
            role = AuthLimits.UserRole,
            isAdmin = true,
            id = 9999,
            passwordHash = "attacker-chosen-hash"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plaintext-to-be-hashed", body);
        Assert.DoesNotContain("attacker-chosen-hash", body);

        // Persisted, and stored as encrypted ciphertext of a hash, never as the plaintext.
        var stored = await ReadStoredHashAsync("persisted.user");
        Assert.NotNull(stored);
        Assert.NotEqual("plaintext-to-be-hashed", stored);
        Assert.DoesNotContain("plaintext-to-be-hashed", stored);
        Assert.DoesNotContain("attacker-chosen-hash", stored);

        // The account answers the login it was created for, and no more.
        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "persisted.user", password = "plaintext-to-be-hashed" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var created = await admin.GetFromJsonAsync<List<UserDto>>("/api/users");
        var user = created!.Single(u => u.Username == "persisted.user");
        Assert.Equal(AuthLimits.UserRole, user.Role);
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_IsRejected()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var first = await admin.PostAsJsonAsync("/api/users",
            new { username = "duplicate.name", password = "first-password-12", role = AuthLimits.UserRole });
        var second = await admin.PostAsJsonAsync("/api/users",
            new { username = "duplicate.name", password = "second-password-1", role = AuthLimits.UserRole });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task CreateUser_MalformedRequests_AreRejected()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var responses = await Task.WhenAll(
            admin.PostAsJsonAsync("/api/users", new { password = "valid-password-1", role = AuthLimits.UserRole }),
            admin.PostAsJsonAsync("/api/users", new { username = "someone", role = AuthLimits.UserRole }),
            admin.PostAsJsonAsync("/api/users", new { username = "someone", password = "short", role = AuthLimits.UserRole }),
            admin.PostAsJsonAsync("/api/users", new { username = "someone", password = "valid-password-1", role = "Superuser" }));

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Login_MissingOrEmptyCredentials_AreRejectedAsBadRequest()
    {
        var client = _fixture.Factory.CreateClient();

        var noPassword = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin" });
        var noUsername = await client.PostAsJsonAsync("/api/auth/login", new { password = "some-password-123" });
        var empty = await client.PostAsJsonAsync("/api/auth/login", new { });

        // Malformed requests are binding failures: 400, not 401, so no authentication
        // information is implied by the distinction.
        Assert.Equal(HttpStatusCode.BadRequest, noPassword.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noUsername.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task DeactivatedUser_ItsIssuedTokenKeepsWorkingUntilExpiry()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var created = await admin.PostAsJsonAsync("/api/users",
            new { username = "tokencarrier", password = "carrier-password-1", role = AuthLimits.UserRole });
        var user = (await created.Content.ReadFromJsonAsync<UserDto>())!;

        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "tokencarrier", password = "carrier-password-1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.Token;

        await admin.PostAsync($"/api/users/{user.Id}/deactivate", null);

        // This asserts the actual guarantee, not the desired one: access tokens are stateless and
        // carry no account state, so deactivation closes login but does not revoke a token already
        // in the wild before its expiry. The day token revocation is implemented, this test must
        // be flipped to 401, and that is exactly why it exists.
        var bearer = _fixture.Factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var stillValid = await bearer.GetAsync("/api/candidates");

        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_SimultaneousRequests_CreateExactlyOneAccount()
    {
        // Reopen bootstrap by emptying the Users table, then race eight distinct accounts against
        // the guard. Distinct usernames matter: identical ones would let the unique index win
        // instead of the guard, and this test exists to prove the guard.
        await ExecuteAsync("DELETE FROM Users");

        var client = _fixture.Factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => client.PostAsJsonAsync(
            "/api/auth/bootstrap",
            new { username = $"racer-{i}", password = "racing-password-1" })));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);

        Assert.Equal(1, created);
        Assert.Equal(7, responses.Length - created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));

        // Exactly one account exists afterwards.
        Assert.Equal(1, await CountUsersAsync());

        // The winner is a racer, so restore the admin the rest of the suite logs in with.
        await ExecuteAsync("DELETE FROM Users");
        var restored = await client.PostAsJsonAsync("/api/auth/bootstrap",
            new { username = "admin", password = SqlApiFixture.AdminPassword });
        Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountUsersAsync()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users";

        return (int)(await command.ExecuteScalarAsync())!;
    }

    /// Reads the raw PasswordHash column as stored, bypassing the API, to prove what is at rest.
    [Fact]
    public async Task TokenWithAnUnsupportedAlgorithm_IsRejected()
    {
        // HS384/HS512 with the correct key and otherwise valid content. The fixture key is 64 bytes
        // so these tokens are constructible; before the algorithm allow-list existed, the API
        // accepted them. The "none" variant is the classic confusion shape: no signature at all.
        var hs384 = CraftToken(_fixture.AuthSigningKeyBytes, algorithm: SecurityAlgorithms.HmacSha384);
        var hs512 = CraftToken(_fixture.AuthSigningKeyBytes, algorithm: SecurityAlgorithms.HmacSha512Signature);
        var unsigned = UnsignedToken();

        foreach (var token in new[] { hs384, hs512, unsigned })
        {
            var client = _fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/candidates")).StatusCode);
        }
    }

    [Fact]
    public async Task ValidlySignedTokens_WithMissingOrInventedRoles_CannotReachTheAdminSurface()
    {
        // Correct key, correct issuer and audience, still authenticated on /api/candidates, but the
        // role claim is absent or invented: authorization must refuse the admin surface with 403.
        var missingRole = CraftToken(_fixture.AuthSigningKeyBytes, role: null);
        var inventedRole = CraftToken(_fixture.AuthSigningKeyBytes, role: "Superuser");

        foreach (var token in new[] { missingRole, inventedRole })
        {
            var client = _fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/candidates")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        }
    }

    [Fact]
    public async Task UsersList_NeverContainsAnyPasswordHash()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var list = await admin.GetAsync("/api/users");
        var body = await list.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);

        // Stronger than a key name check: none of the stored hash values may appear either.
        var storedHash = await ReadStoredHashAsync("admin");
        Assert.NotNull(storedHash);
        Assert.DoesNotContain(storedHash!, body);
    }

    [Fact]
    public async Task EveryProtectedEndpoint_RejectsAnonymousRequests()
    {
        var client = _fixture.Factory.CreateClient();

        var requests = new[]
        {
            client.GetAsync("/api/candidates"),
            client.GetAsync("/api/candidates/1"),
            client.GetAsync("/api/candidates/1/cv"),
            client.PostAsync("/api/candidates", null),
            client.PutAsync("/api/candidates/1", null),
            client.DeleteAsync("/api/candidates/1"),
            client.DeleteAsync("/api/candidates/1/cv"),
            client.GetAsync("/api/degrees"),
            client.PostAsync("/api/degrees", null),
            client.GetAsync("/api/users"),
            client.PostAsync("/api/users", null),
            client.PostAsync("/api/users/1/deactivate", null)
        };

        foreach (var response in await Task.WhenAll(requests))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Authentication_SetsNoCookiesAnywhere()
    {
        // Bearer-only by design: no session cookie means session fixation and CSRF do not apply to
        // this API, and this pin holds until that decision is consciously changed.
        var response = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = SqlApiFixture.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"), "The API must not set authentication cookies.");
    }

    /// A token with alg none and an empty signature: the classic algorithm-confusion shape.
    private static string UnsignedToken()
    {
        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode("{\"sub\":\"1\",\"unique_name\":\"someone\",\"role\":\"Admin\"}");

        return $"{header}.{payload}.";
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

    /// Builds a complete, correctly formed token: the caller decides whether it is valid for this
    /// API by choosing the key, issuer, audience, role and validity window.
    private static string CraftToken(
        byte[] signingKey,
        string issuer = "cv-management-api",
        string audience = "cv-management-client",
        string? role = AuthLimits.AdminRole,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1"),
            new(JwtRegisteredClaimNames.UniqueName, "someone")
        };

        if (role is not null)
        {
            claims.Add(new Claim("role", role));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore ?? now.AddMinutes(-5),
            expires: expires ?? now.AddMinutes(55),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(signingKey), algorithm));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string Base64UrlEncode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string StripTraceId(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);

        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return body;
        }

        return string.Join(",",
            document.RootElement.EnumerateObject()
                .Where(p => p.Name != "traceId")
                .Select(p => $"{p.Name}={p.Value}"));
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenThatUnlocksProtectedEndpoints()
    {
        var anonymous = _fixture.Factory.CreateClient();
        var denied = await anonymous.GetAsync("/api/candidates");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var authenticated = await _fixture.CreateAuthenticatedClientAsync();
        var allowed = await authenticated.GetAsync("/api/candidates");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task UserManagement_RequiresTheAdminRole()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        // Create a plain user through the admin surface.
        var created = await admin.PostAsJsonAsync("/api/users",
            new { username = "reader", password = "reader-password-12", role = AuthLimits.UserRole });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var userClient = await _fixture.CreateUserClientAsync("reader", "reader-password-12");

        var list = await userClient.GetAsync("/api/users");
        var create = await userClient.PostAsJsonAsync("/api/users",
            new { username = "another", password = "another-password-1", role = AuthLimits.UserRole });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        // Candidate data is available to any authenticated account; only user management is role-gated.
        var candidates = await userClient.GetAsync("/api/candidates");
        Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
    }

    [Fact]
    public async Task DeactivatedUser_CannotLogIn()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var created = await admin.PostAsJsonAsync("/api/users",
            new { username = "shortlived", password = "short-lived-password", role = AuthLimits.UserRole });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var user = (await created.Content.ReadFromJsonAsync<UserDto>())!;
        var deactivated = await admin.PostAsync($"/api/users/{user.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);

        var login = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "shortlived", password = "short-lived-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task LastActiveAdministrator_CannotBeDeactivated()
    {
        var admin = await _fixture.CreateAuthenticatedClientAsync();

        var users = await admin.GetFromJsonAsync<List<UserDto>>("/api/users");
        var activeAdmins = users!.Where(u => u.Role == AuthLimits.AdminRole && u.IsActive).ToList();

        if (activeAdmins.Count != 1)
        {
            // Other tests create accounts; only the bootstrap admin exists as an admin here, and
            // this test must never pass by deactivating the only one when others were expected.
            Assert.Fail($"Expected exactly one active admin, found {activeAdmins.Count}.");
        }

        var response = await admin.PostAsync($"/api/users/{activeAdmins[0].Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
