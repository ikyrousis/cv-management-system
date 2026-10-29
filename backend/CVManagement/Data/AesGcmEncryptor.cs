using System.Security.Cryptography;
using System.Text;
using CVManagement.Abstractions;

namespace CVManagement.Data;

/// <summary>AES-256-GCM, random nonce per value, stored as base64 of nonce|tag|ciphertext (strings) or raw nonce|tag|ciphertext (bytes).</summary>
internal sealed class AesGcmEncryptor : IEncryptor, IDisposable
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly AesGcm _aes;

    public AesGcmEncryptor(byte[] key)
    {
        if (key.Length != KeyBytes)
        {
            throw new ArgumentException($"Encryption key must be {KeyBytes} bytes (base64-encoded in configuration).", nameof(key));
        }

        _aes = new AesGcm(key, TagBytes);
    }

    public string EncryptString(string plaintext)
        => Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plaintext)));

    public string DecryptString(string ciphertext)
        => Encoding.UTF8.GetString(DecryptBytes(Convert.FromBase64String(ciphertext)));

    public byte[] EncryptBytes(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        _aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceBytes + TagBytes + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result.AsSpan(NonceBytes));
        ciphertext.CopyTo(result.AsSpan(NonceBytes + TagBytes));

        return result;
    }

    public byte[] DecryptBytes(byte[] payload)
    {
        if (payload.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("Payload is too short to contain a nonce and tag.");
        }

        var nonce = payload.AsSpan(0, NonceBytes);
        var tag = payload.AsSpan(NonceBytes, TagBytes);
        var ciphertext = payload.AsSpan(NonceBytes + TagBytes);
        var plaintext = new byte[ciphertext.Length];

        _aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    public void Dispose() => _aes.Dispose();
}
