using System.Security.Cryptography;
using System.Text;
using CVManagement.Abstractions;

namespace CVManagement.Data;

/// AES-256-GCM with a random nonce per value.
///
/// Versioned payload: marker(1) | nonce(12) | tag(16) | ciphertext, as base64 for strings and raw for bytes.
/// The context string is additional authenticated data, so a value copied into another column fails to
/// verify. Values written before versioning are the unmarked layout nonce(12) | tag(16) | ciphertext with
/// no additional data, and they stay readable: that path can be dropped once no such row remains.
internal sealed class AesGcmEncryptor : IEncryptor, IDisposable
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const byte VersionMarker = 0x01;

    private readonly AesGcm _aes;

    public AesGcmEncryptor(byte[] key)
    {
        if (key.Length != KeyBytes)
        {
            throw new ArgumentException($"Encryption key must be {KeyBytes} bytes (base64-encoded in configuration).", nameof(key));
        }

        _aes = new AesGcm(key, TagBytes);
    }

    public string EncryptString(string plaintext, string context)
        => Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plaintext), context));

    public string DecryptString(string ciphertext, string context)
        => Encoding.UTF8.GetString(DecryptBytes(Convert.FromBase64String(ciphertext), context));

    public byte[] EncryptBytes(byte[] plaintext, string context)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        _aes.Encrypt(nonce, plaintext, ciphertext, tag, AdditionalData(context));

        var result = new byte[1 + NonceBytes + TagBytes + ciphertext.Length];
        result[0] = VersionMarker;
        nonce.CopyTo(result, 1);
        tag.CopyTo(result.AsSpan(1 + NonceBytes));
        ciphertext.CopyTo(result.AsSpan(1 + NonceBytes + TagBytes));

        return result;
    }

    public byte[] DecryptBytes(byte[] payload, string context)
    {
        if (payload.Length > 0 && payload[0] == VersionMarker)
        {
            try
            {
                return DecryptVersioned(payload, context);
            }
            catch (CryptographicException)
            {
                // A legacy payload whose random nonce began with the marker byte. The unmarked layout
                // still verifies it, so the versioned failure is not the answer to report.
            }
        }

        return DecryptLegacy(payload);
    }

    private byte[] DecryptVersioned(byte[] payload, string context)
    {
        if (payload.Length < 1 + NonceBytes + TagBytes)
        {
            throw new CryptographicException("Payload is too short to contain a marker, nonce and tag.");
        }

        var nonce = payload.AsSpan(1, NonceBytes);
        var tag = payload.AsSpan(1 + NonceBytes, TagBytes);
        var ciphertext = payload.AsSpan(1 + NonceBytes + TagBytes);
        var plaintext = new byte[ciphertext.Length];

        _aes.Decrypt(nonce, ciphertext, tag, plaintext, AdditionalData(context));

        return plaintext;
    }

    private byte[] DecryptLegacy(byte[] payload)
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

    private static byte[] AdditionalData(string context) => Encoding.UTF8.GetBytes(context);

    public void Dispose() => _aes.Dispose();
}
