using System.Security.Cryptography;
using System.Text;
using CVManagement.Services;
using CVManagement.Validation;
using Microsoft.Extensions.Options;
using Xunit;

namespace CVManagement.Tests.Unit;

/// A fixed clock for expiry arithmetic; the same shape the token provider tests rely on.
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// The refresh credential format, asserted against independently computed digests and known
/// SHA-256 vectors, never against the implementation's own code path.
public sealed class RefreshTokenGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static RefreshTokenGenerator CreateGenerator(int lifetimeDays = 7)
        => new(
            Microsoft.Extensions.Options.Options.Create(new AuthOptions
            {
                Issuer = "cv-management-api",
                Audience = "cv-management-client",
                SigningKey = Convert.ToBase64String(new byte[32]),
                RefreshTokenLifetimeDays = lifetimeDays
            }),
            new FixedTimeProvider(Now));

    [Fact]
    public void Generate_TwoCallsNeverShareARawToken()
    {
        var generator = CreateGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
    }

    [Fact]
    public void Generate_RawTokenCarries256BitsOfEntropy()
    {
        var material = CreateGenerator().Generate();

        // base64url without padding: 32 bytes encode to 43 characters.
        Assert.Equal(43, material.Token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", material.Token);

        var raw = material.Token.Replace('-', '+').Replace('_', '/');
        var padding = raw.Length % 4 == 2 ? "==" : raw.Length % 4 == 3 ? "=" : "";
        Assert.Equal(AuthLimits.RefreshTokenByteLength, Convert.FromBase64String(raw + padding).Length);
    }

    [Fact]
    public void Generate_TheStoredHashIsTheSha256OfTheRawToken()
    {
        var material = CreateGenerator().Generate();

        // Computed here, independently of the generator: the stored value must be exactly this.
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(material.Token)));

        Assert.Equal(expected, material.TokenHash);
        Assert.NotEqual(material.Token, material.TokenHash);
    }

    [Fact]
    public void Generate_ExpiryIsAFullWindowFromTheClock()
    {
        var material = CreateGenerator(lifetimeDays: 7).Generate();

        Assert.Equal(Now.UtcDateTime, material.CreatedAtUtc);
        Assert.Equal(Now.AddDays(7).UtcDateTime, material.ExpiresAtUtc);
        Assert.True(material.ExpiresAtUtc > material.CreatedAtUtc);
    }

    [Fact]
    public void HashOfAPresentedToken_MatchesTheKnownSha256Vector()
    {
        // SHA-256 of the ASCII string "abc": a published test vector, so the hashing itself is
        // verified against the outside world rather than against a re-run of the implementation.
        var expected = "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=";

        Assert.Equal(expected, CreateGenerator().Hash("abc"));
    }

    [Fact]
    public void Hash_IsDeterministicForTheSameInput()
    {
        var generator = CreateGenerator();

        Assert.Equal(generator.Hash("same-token"), generator.Hash("same-token"));
        Assert.NotEqual(generator.Hash("same-token"), generator.Hash("other-token"));
    }
}
