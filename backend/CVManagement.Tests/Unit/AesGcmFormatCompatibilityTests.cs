using System.Security.Cryptography;
using System.Text;
using CVManagement.Data;
using Xunit;

namespace CVManagement.Tests.Unit;

/// Freezes the stored-data formats with known-answer vectors produced by an independent AES-256-GCM
/// implementation, so a change to the container layout, the encoding or the additional authenticated
/// data fails loudly instead of silently orphaning stored ciphertext.
///
/// Two layouts are in force: the versioned one the writer emits, and the unversioned one already in
/// existing databases, which stays readable.
public sealed class AesGcmFormatCompatibilityTests
{
    // Fixed inputs used to derive the vectors below: key = 32 zero bytes (base64), nonce = 000102...0B.
    private static readonly byte[] Key = Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
    private static readonly byte[] FixedNonce = Convert.FromHexString("000102030405060708090A0B");

    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int VersionedOverhead = 1 + NonceBytes + TagBytes;

    // Legacy layout, no version marker and no additional authenticated data.
    public static IEnumerable<object[]> LegacyPayloadVectors()
    {
        yield return new object[] { "", "AAECAwQFBgcICQoLCrgkutrkiUVTYzAi9xFfcg==" };
        yield return new object[] { "hi", "AAECAwQFBgcICQoL5VyUdW6uAOeNWe5clzaOUeCi" };
        yield return new object[] { "jane@example.com", "AAECAwQFBgcICQoLwsRWmOsF8JLqvvQnQ3FH/uKqXTdAmPeYrwg+gNNgsWE=" };
        yield return new object[] { "The quick brown fox jumps over the lazy dog. 0123456789", "AAECAwQFBgcICQoLeyoOEg4fykrnRhx909lf6dyjVnJxiOaaqVgwl5J0sCwyA9C61ejihrzri33oRy8bhJKcyS0dSNxcv6qkjo1/MAfAoszQtbQ=" };
    }

    // Plaintext paired with the raw AES-256-GCM ciphertext (hex) and tag (hex) for the fixed key and nonce.
    public static IEnumerable<object[]> LegacySegmentVectors()
    {
        yield return new object[] { "", Array.Empty<byte>(), "0AB824BADAE4894553633022F7115F72" };
        yield return new object[] { "hi", Convert.FromHexString("E0A2"), "E55C94756EAE00E78D59EE5C97368E51" };
        yield return new object[] { "jane@example.com", Convert.FromHexString("E2AA5D374098F798AF083E80D360B161"), "C2C45698EB05F092EABEF427437147FE" };
    }

    // Versioned layout: marker 0x01, then nonce, tag and ciphertext, with the column name as AAD.
    public static IEnumerable<object[]> VersionedPayloadVectors()
    {
        yield return new object[] { "", "Candidate.Email", "AQABAgMEBQYHCAkKC1Rkhm1ZNxTEOfQUbQiEWSs=" };
        yield return new object[] { "jane@example.com", "Candidate.Email", "AQABAgMEBQYHCAkKC6jGpxXKA9X0yDsyn3F8ndDiql03QJj3mK8IPoDTYLFh" };
        yield return new object[] { "The quick brown fox jumps over the lazy dog. 0123456789", "Candidate.FirstName", "AQABAgMEBQYHCAkKC0X8QGDzC7iH06Z/HkUYwvjco1ZycYjmmqlYMJeSdLAsMgPQutXo4oa864t96EcvG4SSnMktHUjcXL+qpI6NfzAHwKLM0LW0" };
        yield return new object[] { "wrong column", "Candidate.Mobile", "AQABAgMEBQYHCAkKC1v1UDLgSBxQ1aaY6Pkfjuv/uVw8Z93slq4NP4s=" };
    }

    [Theory]
    [MemberData(nameof(LegacyPayloadVectors))]
    public void DecryptString_KnownAnswerVector_ReturnsExpectedPlaintext(string expectedPlaintext, string payloadBase64)
    {
        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(expectedPlaintext, encryptor.DecryptString(payloadBase64, "Candidate.Email"));
    }

    [Theory]
    [MemberData(nameof(LegacyPayloadVectors))]
    public void DecryptBytes_KnownAnswerPayload_ReturnsExpectedPlaintextUtf8(string expectedPlaintext, string payloadBase64)
    {
        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(Encoding.UTF8.GetBytes(expectedPlaintext), encryptor.DecryptBytes(Convert.FromBase64String(payloadBase64), "Candidate.Email"));
    }

    [Theory]
    [MemberData(nameof(LegacySegmentVectors))]
    public void DecryptBytes_LegacyPayloadBuiltFromReferenceCiphertext_IsStillReadable(string expectedPlaintext, byte[] referenceCiphertext, string referenceTagHex)
    {
        var tag = Convert.FromHexString(referenceTagHex);
        var payload = new byte[NonceBytes + TagBytes + referenceCiphertext.Length];
        FixedNonce.CopyTo(payload, 0);
        tag.CopyTo(payload.AsSpan(NonceBytes));
        referenceCiphertext.CopyTo(payload.AsSpan(NonceBytes + TagBytes));

        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(Encoding.UTF8.GetBytes(expectedPlaintext), encryptor.DecryptBytes(payload, "Candidate.Email"));
    }

    [Theory]
    [MemberData(nameof(VersionedPayloadVectors))]
    public void DecryptBytes_VersionedKnownAnswerVector_ReturnsExpectedPlaintext(string expectedPlaintext, string context, string payloadBase64)
    {
        using var encryptor = new AesGcmEncryptor(Key);

        Assert.Equal(Encoding.UTF8.GetBytes(expectedPlaintext), encryptor.DecryptBytes(Convert.FromBase64String(payloadBase64), context));
    }

    [Theory]
    [MemberData(nameof(VersionedPayloadVectors))]
    public void DecryptBytes_VersionedPayloadFromAnotherColumn_IsRejected(string expectedPlaintext, string context, string payloadBase64)
    {
        var other = context == "Candidate.Email" ? "Candidate.FirstName" : "Candidate.Email";

        using var encryptor = new AesGcmEncryptor(Key);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.DecryptBytes(Convert.FromBase64String(payloadBase64), other));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hi")]
    [InlineData("jane@example.com")]
    [InlineData("The quick brown fox jumps over the lazy dog. 0123456789")]
    public void EncryptBytes_ProducedPayload_IsDecryptableByReferenceAesGcm_WithMarkerNonceTagCiphertextLayout(string plaintext)
    {
        const string context = "Candidate.Email";
        using var encryptor = new AesGcmEncryptor(Key);
        var produced = encryptor.EncryptBytes(Encoding.UTF8.GetBytes(plaintext), context);

        // Split the payload exactly as the documented format prescribes and decrypt it with a plain BCL
        // AesGcm instance. Reordering the segments, dropping the marker or changing the authenticated
        // context makes the reference tag check fail here.
        Assert.Equal(0x01, produced[0]);
        var nonce = produced.AsSpan(1, NonceBytes);
        var tag = produced.AsSpan(1 + NonceBytes, TagBytes);
        var ciphertext = produced.AsSpan(VersionedOverhead);

        using var referenceAes = new AesGcm(Key, TagBytes);
        var recovered = new byte[ciphertext.Length];
        referenceAes.Decrypt(nonce, ciphertext, tag, recovered, Encoding.UTF8.GetBytes(context));

        Assert.Equal(Encoding.UTF8.GetBytes(plaintext), recovered);
    }

    [Fact]
    public void Payload_PrefixIsMarkerNonceAndTag_LengthIsTwentyNineBytesMoreThanPlaintext()
    {
        using var encryptor = new AesGcmEncryptor(Key);
        var plaintext = Encoding.UTF8.GetBytes(new string('x', 100));

        var payload = encryptor.EncryptBytes(plaintext, "Candidate.Email");

        Assert.Equal(VersionedOverhead + plaintext.Length, payload.Length);
        // The nonce is random per call, and because the GCM keystream is derived from the nonce, the
        // ciphertext differs across calls for identical plaintext as well.
        var payload2 = encryptor.EncryptBytes(plaintext, "Candidate.Email");
        Assert.NotEqual(payload[1..(1 + NonceBytes)], payload2[1..(1 + NonceBytes)]);
        Assert.NotEqual(payload[1..], payload2[1..]);
    }

    [Fact]
    public void DecryptString_StringProducedByDifferentInstanceSameKey_Succeeds()
    {
        using var first = new AesGcmEncryptor(Key);
        using var second = new AesGcmEncryptor((byte[])Key.Clone());

        var ciphertext = first.EncryptString("portable@acme.test", "Candidate.Email");

        Assert.Equal("portable@acme.test", second.DecryptString(ciphertext, "Candidate.Email"));
    }

    [Fact]
    public void DecryptBytes_BytesProducedByDifferentInstanceSameKey_Succeeds()
    {
        using var first = new AesGcmEncryptor(Key);
        using var second = new AesGcmEncryptor((byte[])Key.Clone());
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 portable");

        var ciphertext = first.EncryptBytes(content, "Candidate.CVFile");

        Assert.Equal(content, second.DecryptBytes(ciphertext, "Candidate.CVFile"));
    }

    [Fact]
    public void EncryptString_OutputIsStandardBase64_NoLineBreaksNoUrlAlphabet()
    {
        using var encryptor = new AesGcmEncryptor(Key);

        var ciphertext = encryptor.EncryptString(new string('ä', 500), "Candidate.Email");

        Assert.Matches("^[A-Za-z0-9+/]+={0,2}$", ciphertext);
        Assert.Equal(Convert.ToBase64String(Convert.FromBase64String(ciphertext)), ciphertext);
    }
}
