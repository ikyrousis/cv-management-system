namespace CVManagement.Validation;

/// Single declaration of the input patterns shared by every write contract.
public static class Patterns
{
    public const string Email = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

    public const string Mobile = @"^\d{10}$";

    /// Usernames are login identifiers, not free text; keep the character set hostile to tricks.
    public const string Username = @"^[A-Za-z0-9._-]+$";
}
