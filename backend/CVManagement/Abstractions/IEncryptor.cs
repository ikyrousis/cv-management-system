namespace CVManagement.Abstractions;

/// Symmetric encryption at the application boundary; the database only ever stores ciphertext.
///
/// The context is additional authenticated data naming the column a value belongs to, so ciphertext
/// moved to a different column does not verify. It is not secret and must be stable across versions.
public interface IEncryptor
{
    string EncryptString(string plaintext, string context);

    string DecryptString(string ciphertext, string context);

    byte[] EncryptBytes(byte[] plaintext, string context);

    byte[] DecryptBytes(byte[] ciphertext, string context);
}
