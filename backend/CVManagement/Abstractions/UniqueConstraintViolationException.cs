namespace CVManagement.Abstractions;

/// Thrown when a save violates a database uniqueness constraint.
public class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string message, Exception inner) : base(message, inner)
    {
    }
}
