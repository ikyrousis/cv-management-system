using System.Security.Cryptography;
using System.Text;
using CVManagement.Data;
using Xunit;

namespace CVManagement.Tests.Unit;

/// Freezes the stored-data format (nonce|tag|ciphertext, base64, UTF-8) with known-answer
/// vectors produced by an independent AES-256-GCM implementation, so a change to the
/// container layout or encoding fails loudly instead of silently orphaning stored ciphertext.
public sealed class AesGcmFormatCompatibilityTests
{
    // Fixed inputs used to derive the vectors below: key = 32 zero bytes (base64), nonce = 000102...0B.
    private static readonly byte[] Key = Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
    private static readonly byte[] FixedNonce = Convert.FromHexString("000102030405060708090A0B");
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    // Plaintext paired with the full stored payload (base64 of nonce|tag|ciphertext) for the fixed key and nonce.
    public static IEnumerable<object[]> PayloadVectors()
    {
        yield return new object[] { "", "AAECAwQFBgcICQoLCrgkutrkiUVTYzAi9xFfcg==" };
        yield return new object[] { "hi", "AAECAwQFBgcICQoL5VyUdW6uAOeNWe5clzaOUeCi" };
        yield return new object[] { "jane@example.com", "AAECAwQFBgcICQoLwsRWmOsF8JLqvvQnQ3FH/uKqXTdAmPeYrwg+gNNgsWE=" };
        yield return new object[] { "The quick brown fox jumps over the lazy dog. 0123456789", "AAECAwQFBgcICQoLeyoOEg4fykrnRhx909lf6dyjVnJxiOaaqVgwl5J0sCwyA9C61ejihrzri33oRy8bhJKcyS0dSNxcv6qkjo1/MAfAoszQtbQ=" };
    }

    // Plaintext paired with the raw AES-256-GCM ciphertext (hex) and tag (hex) for the fixed key and nonce.
    public static IEnumerable<object[]> SegmentVectors()
    {
        yield return new object[] { "", Array.Empty<byte>(), "0AB824BADAE4894553633022F7115F72" };
        yield return new object[] { "hi", Convert.FromHexString("E0A2"), "E55C94756EAE00E78D59EE5C97368E51" };
        yield return new object[] { "jane@example.com", Convert.FromHexString("E2AA5D374098F798AF083E80D360B161"), "C2C45698EB05F092EABEF427437147FE" };
        yield return new object[]
        {
            "The quick brown fox jumps over the lazy dog. 0123456789",
            Convert.FromHexString("DCA356727188E69AA95830979274B02C3203D0BAD5E8E286BCEB8B7DE8472F1B84929CC92D1D48DC5CBFAAA48E8D7F3007C0A2CCD0B5B4"),
            "7B2A0E120E1FCA4AE7461C7DD3D95FE9",
        };
    }

    [Theory]
    [MemberData(nameof(PayloadVectors))]
    public void DecryptString_KnownAnswerVector_ReturnsExpectedPlaintext(string expectedPlaintext, string payloadBase64)
    {
        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(expectedPlaintext, encryptor.DecryptString(payloadBase64));
    }

    [Theory]
    [MemberData(nameof(PayloadVectors))]
    public void DecryptBytes_KnownAnswerPayload_ReturnsExpectedPlaintextUtf8(string expectedPlaintext, string payloadBase64)
    {
        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(Encoding.UTF8.GetBytes(expectedPlaintext), encryptor.DecryptBytes(Convert.FromBase64String(payloadBase64)));
    }

    [Theory]
    [MemberData(nameof(SegmentVectors))]
    public void DecryptBytes_PayloadBuiltFromReferenceCiphertext_AcceptsNonceTagCiphertextLayout(string expectedPlaintext, byte[] referenceCiphertext, string referenceTagHex)
    {
        var tag = Convert.FromHexString(referenceTagHex);
        var payload = new byte[NonceBytes + TagBytes + referenceCiphertext.Length];
        FixedNonce.CopyTo(payload, 0);
        tag.CopyTo(payload.AsSpan(NonceBytes));
        referenceCiphertext.CopyTo(payload.AsSpan(NonceBytes + TagBytes));

        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(Encoding.UTF8.GetBytes(expectedPlaintext), encryptor.DecryptBytes(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hi")]
    [InlineData("jane@example.com")]
    [InlineData("The quick brown fox jumps over the lazy dog. 0123456789")]
    public void EncryptBytes_ProducedPayload_IsDecryptableByReferenceAesGcm_WithNonceTagCiphertextLayout(string plaintext)
    {
        using var encryptor = new AesGcmEncryptor(Key);
        var produced = encryptor.EncryptBytes(Encoding.UTF8.GetBytes(plaintext));

        // Split the payload exactly as the documented format prescribes and decrypt it with a
        // plain BCL AesGcm instance. Any reorder or resize of the segments, or an extra
        // transform applied to the ciphertext, makes the reference tag check fail here.
        var nonce = produced.AsSpan(0, NonceBytes);
        var tag = produced.AsSpan(NonceBytes, TagBytes);
        var ciphertext = produced.AsSpan(NonceBytes + TagBytes);

        using var referenceAes = new AesGcm(Key, TagBytes);
        var recovered = new byte[ciphertext.Length];
        referenceAes.Decrypt(nonce, ciphertext, tag, recovered);

        Assert.Equal(Encoding.UTF8.GetBytes(plaintext), recovered);
    }

    [Fact]
    public void Payload_PrefixIsNonceAndTag_LengthIsExactlyTwentyEightBytesMoreThanPlaintext()
    {
        using var encryptor = new AesGcmEncryptor(Key);
        var plaintext = Encoding.UTF8.GetBytes(new string('x', 100));

        var payload = encryptor.EncryptBytes(plaintext);

        Assert.Equal(NonceBytes + TagBytes + plaintext.Length, payload.Length);
        // The nonce is random per call, and because the GCM keystream is derived from the
        // nonce, the ciphertext differs across calls for identical plaintext as well.
        var payload2 = encryptor.EncryptBytes(plaintext);
        Assert.NotEqual(payload[..NonceBytes], payload2[..NonceBytes]);
        Assert.NotEqual(payload[NonceBytes..], payload2[NonceBytes..]);
    }

    [Fact]
    public void DecryptString_StringProducedByDifferentInstanceSameKey_Succeeds()
    {
        using var first = new AesGcmEncryptor(Key);
        using var second = new AesGcmEncryptor((byte[])Key.Clone());

        var ciphertext = first.EncryptString("portable@acme.test");

        Assert.Equal("portable@acme.test", second.DecryptString(ciphertext));
    }

    [Fact]
    public void DecryptBytes_BytesProducedByDifferentInstanceSameKey_Succeeds()
    {
        using var first = new AesGcmEncryptor(Key);
        using var second = new AesGcmEncryptor((byte[])Key.Clone());
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 portable");

        var ciphertext = first.EncryptBytes(content);

        Assert.Equal(content, second.DecryptBytes(ciphertext));
    }

    [Fact]
    public void EncryptString_OutputIsStandardBase64_NoLineBreaksNoUrlAlphabet()
    {
        using var encryptor = new AesGcmEncryptor(Key);

        var ciphertext = encryptor.EncryptString(new string('ä', 500));

        Assert.Matches("^[A-Za-z0-9+/]+={0,2}$", ciphertext);
        Assert.Equal(Convert.ToBase64String(Convert.FromBase64String(ciphertext)), ciphertext);
    }
}
