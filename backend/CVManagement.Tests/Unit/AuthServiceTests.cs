using System.IdentityModel.Tokens.Jwt;
using CVManagement.Abstractions;
using CVManagement.DTOs;
using CVManagement.Entities;
using CVManagement.Results;
using CVManagement.Services;
using CVManagement.Validation;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class AuthServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITokenProvider _tokens = Substitute.For<ITokenProvider>();
    private readonly IRefreshTokenGenerator _refreshGenerator = Substitute.For<IRefreshTokenGenerator>();
    private readonly IPasswordHasher<User> _hasher = Substitute.For<IPasswordHasher<User>>();
    private readonly TimeProvider _clock = new FixedTimeProvider(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private AuthService CreateSut()
        => new(_users, _refreshTokens, _unitOfWork, _tokens, _refreshGenerator, _hasher, _clock);

    private static readonly AccessTokenResponse Token = new("token", DateTime.UtcNow.AddHours(1));

    private User ActiveUser(string passwordHash) => new()
    {
        Id = 1,
        Username = "admin",
        PasswordHash = passwordHash,
        Role = AuthLimits.AdminRole,
        IsActive = true
    };

    [Fact]
    public async Task LoginAsync_UnknownUserAndWrongPasswordAndDeactivatedAccount_FailIdentically()
    {
        // Three different causes, and the failure must be indistinguishable in kind and message,
        // because the response is the oracle an attacker reads.
        _users.GetByUsernameAsync("nobody", Arg.Any<CancellationToken>()).Returns((User?)null);

        var wrongPasswordUser = ActiveUser("hash");
        _users.GetByUsernameAsync("admin", Arg.Any<CancellationToken>()).Returns(wrongPasswordUser);
        _hasher.VerifyHashedPassword(wrongPasswordUser, "hash", "wrong-password")
            .Returns(PasswordVerificationResult.Failed);

        var deactivated = new User
        {
            Id = 3,
            Username = "admin",
            PasswordHash = "hash",
            Role = AuthLimits.AdminRole,
            IsActive = false
        };
        _users.GetByUsernameAsync("frozen", Arg.Any<CancellationToken>()).Returns(deactivated);

        var s = CreateSut();

        var unknown = await s.LoginAsync(new LoginRequest { Username = "nobody", Password = "whatever-password" });
        var wrong = await s.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong-password" });
        var frozen = await s.LoginAsync(new LoginRequest { Username = "frozen", Password = "whatever-password" });

        foreach (var result in new[] { unknown, wrong, frozen })
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(FailureKind.Unauthorized, result.Failure.Kind);
            Assert.Equal(unknown.Failure.Message, result.Failure.Message);
        }

        _tokens.DidNotReceive().IssueToken(Arg.Any<User>());
    }

    [Fact]
    public async Task LoginAsync_ActiveUserWithCorrectPassword_ReceivesToken()
    {
        var user = ActiveUser("hash");
        _users.GetByUsernameAsync("admin", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.VerifyHashedPassword(user, "hash", "correct-password").Returns(PasswordVerificationResult.Success);
        _tokens.IssueToken(user).Returns(Token);
        _refreshGenerator.Generate().Returns(RefreshMaterial());

        var result = await CreateSut().LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        Assert.True(result.IsSuccess);
        Assert.Equal("token", result.Value!.Token);
    }

    [Fact]
    public async Task LoginAsync_StoresARefreshTokenAndReturnsItExactlyOnce()
    {
        var user = ActiveUser("hash");
        _users.GetByUsernameAsync("admin", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.VerifyHashedPassword(user, "hash", "correct-password").Returns(PasswordVerificationResult.Success);
        _tokens.IssueToken(user).Returns(Token);
        var material = RefreshMaterial();
        _refreshGenerator.Generate().Returns(material);

        var result = await CreateSut().LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        Assert.True(result.IsSuccess);
        Assert.Equal(material.Token, result.Value!.RefreshToken);
        Assert.Equal(material.ExpiresAtUtc, result.Value!.RefreshExpiresAtUtc);

        // Only the digest may be persisted, bound to the account that just logged in.
        _refreshTokens.Received(1).Add(Arg.Is<RefreshToken>(t =>
            t.UserId == user.Id && t.TokenHash == material.TokenHash && t.ExpiresAtUtc == material.ExpiresAtUtc));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BootstrapAsync_StoresARefreshTokenForTheNewAdministrator()
    {
        var material = RefreshMaterial();
        _refreshGenerator.Generate().Returns(material);
        _users.AddWhenEmptyAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(true);
        _hasher.HashPassword(Arg.Any<User>(), "bootstrap-password").Returns("hash");
        _tokens.IssueToken(Arg.Any<User>()).Returns(Token);

        var result = await CreateSut().BootstrapAsync(
            new BootstrapRequest { Username = "admin", Password = "bootstrap-password" });

        Assert.True(result.IsSuccess);
        Assert.Equal(material.Token, result.Value!.RefreshToken);
        _refreshTokens.Received(1).Add(Arg.Is<RefreshToken>(t => t.TokenHash == material.TokenHash));
    }

    private static RefreshTokenMaterial RefreshMaterial()
        => new("raw-token", "stored-hash", CreatedAtUtc, ExpiresAtUtc);

    private static readonly DateTime CreatedAtUtc = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ExpiresAtUtc = CreatedAtUtc.AddDays(7);

    private RefreshToken StoredToken(int userId = 1, DateTime? revokedAtUtc = null)
        => new()
        {
            Id = 5,
            UserId = userId,
            TokenHash = "stored-hash",
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc,
            RevokedAtUtc = revokedAtUtc
        };

    [Fact]
    public async Task RefreshAsync_UnknownToken_IsRefusedWithoutContainment()
    {
        _refreshGenerator.Hash("unknown-token").Returns("unknown-hash");
        _refreshTokens.FindByHashAsync("unknown-hash", Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var result = await CreateSut().RefreshAsync(new RefreshRequest { RefreshToken = "unknown-token" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Unauthorized, result.Failure.Kind);

        // An unknown token is not evidence of theft: the account's other tokens must survive it.
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _tokens.DidNotReceive().IssueToken(Arg.Any<User>());
    }

    [Fact]
    public async Task RefreshAsync_ActiveToken_IsConsumedAndReplacedByANewPair()
    {
        var user = ActiveUser("hash");
        var stored = StoredToken();
        _refreshGenerator.Hash("raw-token").Returns("stored-hash");
        _refreshTokens.FindByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns(stored);
        _users.FindAsync(1, Arg.Any<CancellationToken>()).Returns(user);
        var material = RefreshMaterial();
        _refreshGenerator.Generate().Returns(material);
        _refreshTokens
            .TryReplaceAsync("stored-hash", Arg.Any<DateTime>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _tokens.IssueToken(user).Returns(Token);

        var result = await CreateSut().RefreshAsync(new RefreshRequest { RefreshToken = "raw-token" });

        Assert.True(result.IsSuccess);
        Assert.Equal("token", result.Value!.Token);
        Assert.Equal(material.Token, result.Value!.RefreshToken);

        // The old credential died in the same committed exchange that stored the new one.
        await _refreshTokens.Received(1).TryReplaceAsync(
            "stored-hash",
            Arg.Any<DateTime>(),
            Arg.Is<RefreshToken>(t => t.TokenHash == material.TokenHash && t.UserId == user.Id),
            Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_AlreadyRevokedToken_RevokesTheWholeChain()
    {
        var revoked = StoredToken(revokedAtUtc: CreatedAtUtc.AddMinutes(5));
        _refreshGenerator.Hash("stolen-token").Returns("stored-hash");
        _refreshTokens.FindByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns(revoked);

        var result = await CreateSut().RefreshAsync(new RefreshRequest { RefreshToken = "stolen-token" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Unauthorized, result.Failure.Kind);

        // Only the thief can be holding a used token, so the account loses every token it holds.
        await _refreshTokens.Received(1).RevokeAllForUserAsync(revoked.UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _tokens.DidNotReceive().IssueToken(Arg.Any<User>());
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_IsRefusedWithoutContainment()
    {
        var expired = new RefreshToken
        {
            Id = 6,
            UserId = 1,
            TokenHash = "stored-hash",
            CreatedAtUtc = CreatedAtUtc.AddDays(-10),
            ExpiresAtUtc = CreatedAtUtc.AddDays(-3)
        };
        _refreshGenerator.Hash("old-token").Returns("stored-hash");
        _refreshTokens.FindByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns(expired);

        var result = await CreateSut().RefreshAsync(new RefreshRequest { RefreshToken = "old-token" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Unauthorized, result.Failure.Kind);

        // Expiry is abandonment, not theft: the account's other tokens survive it.
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _tokens.DidNotReceive().IssueToken(Arg.Any<User>());
    }

    [Fact]
    public async Task RefreshAsync_DeactivatedAccount_IsRefused()
    {
        var user = new User { Id = 1, Username = "frozen", Role = AuthLimits.UserRole, IsActive = false };
        _refreshGenerator.Hash("raw-token").Returns("stored-hash");
        _refreshTokens.FindByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns(StoredToken());
        _users.FindAsync(1, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateSut().RefreshAsync(new RefreshRequest { RefreshToken = "raw-token" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Unauthorized, result.Failure.Kind);

        // Refused before the exchange: deactivation has already revoked the account's tokens, so
        // a dead account cannot mint a session or a fresh credential.
        await _refreshTokens.DidNotReceive().TryReplaceAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        _tokens.DidNotReceive().IssueToken(Arg.Any<User>());
    }

    [Fact]
    public async Task RefreshAsync_AnotherRequestConsumedTheTokenFirst_TriggersContainment()
    {
        _refreshGenerator.Hash("raced-token").Returns("stored-hash");
        _refreshTokens.FindByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns(StoredToken());
        _users.FindAsync(1, Arg.Any<CancellationToken>()).Returns(ActiveUser("hash"));
        _refreshGenerator.Generate().Returns(RefreshMaterial());
        // The serialized exchange reports that this call was not the one that consumed the token.
        _refreshTokens
            .TryReplaceAsync("stored-hash", Arg.Any<DateTime>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateSut().RefreshAsync(new RefreshRequest { RefreshToken = "raced-token" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Unauthorized, result.Failure.Kind);

        // Two presentations of one token means the credential leaked: kill the chain.
        await _refreshTokens.Received(1).RevokeAllForUserAsync(1, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _tokens.DidNotReceive().IssueToken(Arg.Any<User>());
    }

    [Fact]
    public async Task RevokeAsync_RevokesThePresentedToken()
    {
        _refreshGenerator.Hash("raw-token").Returns("stored-hash");

        var result = await CreateSut().RevokeAsync("raw-token");

        Assert.True(result.IsSuccess);
        await _refreshTokens.Received(1).TryRevokeByHashAsync("stored-hash", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_SucceedsWithoutContainment()
    {
        _refreshGenerator.Hash("bogus-token").Returns("unknown-hash");
        _refreshTokens.TryRevokeByHashAsync("unknown-hash", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut().RevokeAsync("bogus-token");

        // Logout must not answer with an oracle, and a stray token is not theft evidence.
        Assert.True(result.IsSuccess);
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_AlsoRevokesTheAccountsRefreshTokens()
    {
        var admin = ActiveUser("hash");
        _users.FindAsync(1, Arg.Any<CancellationToken>()).Returns(admin);
        _users.CountActiveInRoleAsync(AuthLimits.AdminRole, Arg.Any<CancellationToken>()).Returns(2);

        var result = await CreateSut().DeactivateAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(admin.IsActive);

        // Deactivation must close the refresh path immediately, not just future logins.
        await _refreshTokens.Received(1).RevokeAllForUserAsync(1, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_HashNeedsRehash_RehashesBeforeIssuingToken()
    {
        var user = ActiveUser("old-format");
        _users.GetByUsernameAsync("admin", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.VerifyHashedPassword(user, "old-format", "correct-password")
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);
        _hasher.HashPassword(user, "correct-password").Returns("new-format");
        _tokens.IssueToken(Arg.Any<User>()).Returns(Token);
        _refreshGenerator.Generate().Returns(RefreshMaterial());

        var result = await CreateSut().LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        Assert.True(result.IsSuccess);
        Assert.Equal("new-format", user.PasswordHash);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BootstrapAsync_EmptyStore_CreatesAdminAndIssuesToken()
    {
        User? created = null;
        _users.AddWhenEmptyAsync(Arg.Do<User>(u => created = u), Arg.Any<CancellationToken>()).Returns(true);
        _hasher.HashPassword(Arg.Any<User>(), "bootstrap-password").Returns("hash");
        _tokens.IssueToken(Arg.Any<User>()).Returns(Token);
        _refreshGenerator.Generate().Returns(RefreshMaterial());

        var result = await CreateSut().BootstrapAsync(
            new BootstrapRequest { Username = "admin", Password = "bootstrap-password" });

        Assert.True(result.IsSuccess);
        Assert.NotNull(created);
        Assert.Equal("admin", created!.Username);
        Assert.Equal(AuthLimits.AdminRole, created.Role);
        Assert.True(created.IsActive);
        Assert.Equal("hash", created.PasswordHash);
    }

    [Fact]
    public async Task BootstrapAsync_NonEmptyStore_Refuses()
    {
        _users.AddWhenEmptyAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut().BootstrapAsync(
            new BootstrapRequest { Username = "admin", Password = "bootstrap-password" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
    }

    [Fact]
    public async Task CreateUserAsync_UnknownRole_IsRejected()
    {
        var result = await CreateSut().CreateUserAsync(new CreateUserRequest
        {
            Username = "someone",
            Password = "whatever-password",
            Role = "Superuser"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task CreateUserAsync_TakenUsername_IsRejectedWithoutHashing()
    {
        _users.GetByUsernameAsync("taken", Arg.Any<CancellationToken>())
            .Returns(new User { Username = "taken", Role = AuthLimits.UserRole });

        var result = await CreateSut().CreateUserAsync(new CreateUserRequest
        {
            Username = "taken",
            Password = "whatever-password",
            Role = AuthLimits.UserRole
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        _hasher.DidNotReceive().HashPassword(Arg.Any<User>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DeactivateAsync_LastActiveAdministrator_IsRefused()
    {
        var admin = ActiveUser("hash");
        _users.FindAsync(1, Arg.Any<CancellationToken>()).Returns(admin);
        _users.CountActiveInRoleAsync(AuthLimits.AdminRole, Arg.Any<CancellationToken>()).Returns(1);

        var result = await CreateSut().DeactivateAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_AnotherAdminRemains_SucceedsAndPersists()
    {
        var admin = ActiveUser("hash");
        _users.FindAsync(1, Arg.Any<CancellationToken>()).Returns(admin);
        _users.CountActiveInRoleAsync(AuthLimits.AdminRole, Arg.Any<CancellationToken>()).Returns(2);

        var result = await CreateSut().DeactivateAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(admin.IsActive);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class JwtTokenProviderTests
{
    private static JwtTokenProvider CreateProvider(int lifetimeMinutes = 60)
        => new(
            Microsoft.Extensions.Options.Options.Create(new AuthOptions
            {
                Issuer = "cv-management-api",
                Audience = "cv-management-client",
                SigningKey = Convert.ToBase64String(new byte[32]),
                AccessTokenLifetimeMinutes = lifetimeMinutes
            }),
            TimeProvider.System);

    [Fact]
    public void IssueToken_SignsIdentityRoleAndExpiry()
    {
        var user = new User { Id = 7, Username = "admin", Role = AuthLimits.AdminRole };

        var response = CreateProvider().IssueToken(user);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        Assert.Equal("cv-management-api", token.Issuer);
        Assert.Equal("cv-management-client", token.Audiences.Single());
        Assert.Equal("7", token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("admin", token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.UniqueName).Value);
        Assert.Equal(AuthLimits.AdminRole, token.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("HS256", token.SignatureAlgorithm);
    }

    [Fact]
    public void IssueToken_ExpiryMatchesTheConfiguredLifetime()
    {
        var before = DateTime.UtcNow.AddMinutes(-1);
        var response = CreateProvider(lifetimeMinutes: 15).IssueToken(new User { Id = 1, Username = "u", Role = AuthLimits.UserRole });
        var after = DateTime.UtcNow.AddMinutes(1);

        Assert.InRange(response.ExpiresAtUtc, before.AddMinutes(15), after.AddMinutes(15));
    }
}

/// Framework behaviour the security claims rest on: a fresh random salt per hash, verified for
/// correct passwords, rejected for wrong ones, at the configured work factor.
public sealed class PasswordHasherTests
{
    private static PasswordHasher<User> CreateHasher()
        => new(Microsoft.Extensions.Options.Options.Create(
            new PasswordHasherOptions { IterationCount = AuthLimits.Pbkdf2Iterations }));

    private static User AnyUser() => new() { Id = 1, Username = "u", Role = AuthLimits.UserRole };

    [Fact]
    public void StoredHash_IsNotThePlaintext()
    {
        var hash = CreateHasher().HashPassword(AnyUser(), "secret-password");

        Assert.NotEqual("secret-password", hash);
        Assert.DoesNotContain("secret-password", hash);
    }

    [Fact]
    public void SamePasswordTwice_ProducesDifferentHashes_AndBothVerify()
    {
        var hasher = CreateHasher();

        var first = hasher.HashPassword(AnyUser(), "shared-password");
        var second = hasher.HashPassword(AnyUser(), "shared-password");

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(AnyUser(), first, "shared-password"));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(AnyUser(), second, "shared-password"));
    }

    [Fact]
    public void WrongPassword_FailsVerification()
    {
        var hasher = CreateHasher();
        var hash = hasher.HashPassword(AnyUser(), "right-password");

        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(AnyUser(), hash, "wrong-password"));
    }
}
