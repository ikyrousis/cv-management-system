namespace CVManagement.Validation;

/// Policy names, declared once so Program.cs and the controllers cannot disagree.
public static class RateLimitPolicy
{
    /// Covers the authentication endpoints: login and first-run bootstrap.
    public const string Auth = "auth";
}
