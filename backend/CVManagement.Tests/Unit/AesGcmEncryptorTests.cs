using System.Security.Cryptography;
using System.Text;
using CVManagement.Data;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class AesGcmEncryptorTests : IDisposable
{
    private const string Context = "Candidate.Email";

    /// Versioned payload: marker(1) | nonce(12) | tag(16) | ciphertext.
    private const int NonceStart = 1;
    private const int TagStart = 13;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly AesGcmEncryptor _encryptor;

    public AesGcmEncryptorTests() => _encryptor = new AesGcmEncryptor(_key);

    public void Dispose() => _encryptor.Dispose();

    [Fact]
    public void StringRoundTrip_ReturnsPlaintext_AndCiphertextHidesIt()
    {
        var ciphertext = _encryptor.EncryptString("jane@example.com", Context);

        Assert.DoesNotContain("@", ciphertext);
        Assert.Equal("jane@example.com", _encryptor.DecryptString(ciphertext, Context));
    }

    [Fact]
    public void BytesRoundTrip_ReturnsOriginal()
    {
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 fake cv content");

        var ciphertext = _encryptor.EncryptBytes(content, "Candidate.CVFile");

        Assert.NotEqual(content, ciphertext);
        Assert.Equal(content, _encryptor.DecryptBytes(ciphertext, "Candidate.CVFile"));
    }

    [Fact]
    public void SameInputHundredTimes_ProducesDistinctCiphertexts()
    {
        var outputs = Enumerable.Range(0, 100)
            .Select(_ => _encryptor.EncryptString("same input", Context))
            .Distinct()
            .ToList();

        Assert.Equal(100, outputs.Count);
    }

    [Theory]
    [InlineData(0, "version marker")]
    [InlineData(NonceStart, "nonce byte")]
    [InlineData(TagStart, "tag byte")]
    [InlineData(29, "first ciphertext byte")]
    [InlineData(44, "last ciphertext byte")]
    public void SingleByteFlipAtAnySegment_FailsTagCheck(int position, string _)
    {
        var payload = _encryptor.EncryptBytes(new byte[16], Context);
        payload[position] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptBytes(payload, Context));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(27)]
    [InlineData(28)]
    public void TruncatedBelowTheFullHeader_Rejected(int truncateTo)
    {
        var payload = _encryptor.EncryptBytes(new byte[] { 1, 2, 3, 4 }, Context);

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptBytes(payload[..truncateTo], Context));
    }

    [Fact]
    public void EmptyPlaintext_StoresMarkerNonceAndTag()
    {
        var payload = _encryptor.EncryptBytes(Array.Empty<byte>(), Context);

        Assert.Equal(29, payload.Length);
        Assert.Equal(0x01, payload[0]);
        Assert.Empty(_encryptor.DecryptBytes(payload, Context));
    }

    [Theory]
    [InlineData("Candidate.Email", "Candidate.FirstName")]
    [InlineData("Candidate.CVFile", "Candidate.CVFileName")]
    [InlineData("Candidate.Mobile", "Candidate.LastName")]
    public void ValueEncryptedForOneColumn_DoesNotVerifyInAnother(string written, string read)
    {
        var payload = _encryptor.EncryptBytes(Encoding.UTF8.GetBytes("ada@example.com"), written);

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptBytes(payload, read));
    }

    [Fact]
    public void CorruptedBase64String_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _encryptor.DecryptString("!!!not-base64!!!", Context));
    }

    [Fact]
    public void ValidButForeignBase64String_FailsTagCheck()
    {
        var foreign = Convert.ToBase64String(RandomNumberGenerator.GetBytes(60));

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptString(foreign, Context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("é")]
    [InlineData("中文 èspresso")]
    [InlineData("emoji \U0001F600 \U0001F3C6")]
    [InlineData("a-long-ish.email+tag@sub.domain.example.co.uk")]
    public void AwkwardStrings_RoundTrip(string plaintext)
    {
        Assert.Equal(plaintext, _encryptor.DecryptString(_encryptor.EncryptString(plaintext, Context), Context));
    }

    [Fact]
    public void OneMegabyte_RoundTripsIntact()
    {
        var big = RandomNumberGenerator.GetBytes(1024 * 1024);

        Assert.Equal(big, _encryptor.DecryptBytes(_encryptor.EncryptBytes(big, Context), Context));
    }

    [Fact]
    public void KeyBitFlip_CannotDecryptAnotherKeysOutput()
    {
        var tamperedKey = (byte[])_key.Clone();
        tamperedKey[0] ^= 0x01;

        using var other = new AesGcmEncryptor(tamperedKey);
        var ciphertext = _encryptor.EncryptString("secret", Context);

        Assert.ThrowsAny<CryptographicException>(() => other.DecryptString(ciphertext, Context));
    }

    [Fact]
    public void WrongKeyLengths_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptor(new byte[16]));
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptor(new byte[31]));
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptor(new byte[33]));
    }

    [Fact]
    public void TwoHundredConcurrentRoundTrips_OnSharedInstance_AllSucceed()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                var text = $"concurrent payload {i}";
                if (_encryptor.DecryptString(_encryptor.EncryptString(text, Context), Context) != text)
                {
                    failures.Add(i);
                }
            }
            catch
            {
                failures.Add(i);
            }
        });

        Assert.Empty(failures);
    }
}
