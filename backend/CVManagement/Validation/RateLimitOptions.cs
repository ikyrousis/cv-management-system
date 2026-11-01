namespace CVManagement.Validation;

/// Bound from the RateLimit configuration section. The window and permit count are deployment
/// policy, not code, so they can be tuned without a release and tightened in tests.
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// Login attempts allowed per window, per source address.
    public int AuthPermitLimit { get; set; } = 5;

    public int AuthWindowSeconds { get; set; } = 900;
}
