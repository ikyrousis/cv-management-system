namespace CVManagement.Abstractions;

/// <summary>Thrown when a save violates a database uniqueness constraint.</summary>
public class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string message, Exception inner) : base(message, inner)
    {
    }
}
