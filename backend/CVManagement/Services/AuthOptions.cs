using CVManagement.Validation;

namespace CVManagement.Services;

/// Bound from the CvAuth configuration section. The signing key follows the same rule as the
/// encryption key: configuration or user-secrets in Development, environment variable in Docker,
/// never committed.
public sealed class AuthOptions
{
    public const string SectionName = "CvAuth";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// Base64-encoded, at least 32 bytes; generated with 'openssl rand -base64 32'.
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; set; } = AuthLimits.DefaultAccessTokenLifetimeMinutes;

    public byte[] SigningKeyBytes => Convert.FromBase64String(SigningKey);
}
