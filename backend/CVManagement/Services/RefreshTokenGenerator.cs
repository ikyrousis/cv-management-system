using System.Security.Cryptography;
using System.Text;
using CVManagement.Validation;
using Microsoft.Extensions.Options;

namespace CVManagement.Services;

/// Issues the rotating refresh credential: 256 bits of CSPRNG output, base64url on the wire, and
/// only its SHA-256 hash ever reaches persistence.
public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public RefreshTokenGenerator(IOptions<AuthOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public RefreshTokenMaterial Generate()
    {
        var now = _clock.GetUtcNow();

        var raw = RandomNumberGenerator.GetBytes(AuthLimits.RefreshTokenByteLength);
        var token = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return new RefreshTokenMaterial(
            token,
            Hash(token),
            now.UtcDateTime,
            now.AddDays(_options.RefreshTokenLifetimeDays).UtcDateTime);
    }

    public string Hash(string rawToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
