namespace CVManagement.Abstractions;

/// Symmetric encryption at the application boundary; the database only ever stores ciphertext.
public interface IEncryptor
{
    string EncryptString(string plaintext);

    string DecryptString(string ciphertext);

    byte[] EncryptBytes(byte[] plaintext);

    byte[] DecryptBytes(byte[] ciphertext);
}
