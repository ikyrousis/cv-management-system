namespace CVManagement.Validation;

/// <summary>Single declaration of the input patterns shared by every write contract.</summary>
public static class Patterns
{
    public const string Email = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

    public const string Mobile = @"^\d{10}$";
}
