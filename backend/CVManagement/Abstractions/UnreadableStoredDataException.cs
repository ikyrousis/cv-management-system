namespace CVManagement.Abstractions;

/// Stored data that cannot be decrypted. Raised where the row is known, so the numeric id can reach the
/// log without the protected content ever being named.
public sealed class UnreadableStoredDataException(string message, int? id, Exception inner) : Exception(message, inner)
{
    /// The numeric id of the row that could not be read, when it is known. Never a stored value.
    public int? Id { get; } = id;
}
