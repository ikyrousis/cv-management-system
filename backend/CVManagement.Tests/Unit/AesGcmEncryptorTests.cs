using System.Security.Cryptography;
using System.Text;
using CVManagement.Data;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class AesGcmEncryptorTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly AesGcmEncryptor _encryptor;

    public AesGcmEncryptorTests() => _encryptor = new AesGcmEncryptor(_key);

    public void Dispose() => _encryptor.Dispose();

    [Fact]
    public void StringRoundTrip_ReturnsPlaintext_AndCiphertextHidesIt()
    {
        var ciphertext = _encryptor.EncryptString("jane@example.com");

        Assert.DoesNotContain("@", ciphertext);
        Assert.Equal("jane@example.com", _encryptor.DecryptString(ciphertext));
    }

    [Fact]
    public void BytesRoundTrip_ReturnsOriginal()
    {
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 fake cv content");

        var ciphertext = _encryptor.EncryptBytes(content);

        Assert.NotEqual(content, ciphertext);
        Assert.Equal(content, _encryptor.DecryptBytes(ciphertext));
    }

    [Fact]
    public void SameInputHundredTimes_ProducesDistinctCiphertexts()
    {
        var outputs = Enumerable.Range(0, 100)
            .Select(_ => _encryptor.EncryptString("same input"))
            .Distinct()
            .ToList();

        Assert.Equal(100, outputs.Count);
    }

    [Theory]
    [InlineData(0, "nonce byte")]
    [InlineData(12, "tag byte")]
    [InlineData(28, "first ciphertext byte")]
    [InlineData(43, "last ciphertext byte")]
    public void SingleByteFlipAtAnySegment_FailsTagCheck(int position, string _)
    {
        var payload = _encryptor.EncryptBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        payload[position] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptBytes(payload));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(27)]
    public void TruncatedBelowNoncePlusTag_Rejected(int truncateTo)
    {
        var payload = _encryptor.EncryptBytes(new byte[] { 1, 2, 3, 4 });

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptBytes(payload[..truncateTo]));
    }

    [Fact]
    public void EmptyPlaintext_StoresExactlyNonceAndTag()
    {
        var payload = _encryptor.EncryptBytes(Array.Empty<byte>());

        Assert.Equal(28, payload.Length);
        Assert.Empty(_encryptor.DecryptBytes(payload));
    }

    [Fact]
    public void CorruptedBase64String_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _encryptor.DecryptString("!!!not-base64!!!"));
    }

    [Fact]
    public void ValidButForeignBase64String_FailsTagCheck()
    {
        var foreign = Convert.ToBase64String(RandomNumberGenerator.GetBytes(60));

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptString(foreign));
    }

    [Theory]
    [InlineData("")]
    [InlineData("é")]
    [InlineData("中文 èspresso")]
    [InlineData("emoji \U0001F600 \U0001F3C6")]
    [InlineData("a-long-ish.email+tag@sub.domain.example.co.uk")]
    public void AwkwardStrings_RoundTrip(string plaintext)
    {
        Assert.Equal(plaintext, _encryptor.DecryptString(_encryptor.EncryptString(plaintext)));
    }

    [Fact]
    public void OneMegabyte_RoundTripsIntact()
    {
        var big = RandomNumberGenerator.GetBytes(1024 * 1024);

        Assert.Equal(big, _encryptor.DecryptBytes(_encryptor.EncryptBytes(big)));
    }

    [Fact]
    public void KeyBitFlip_CannotDecryptAnotherKeysOutput()
    {
        var tamperedKey = (byte[])_key.Clone();
        tamperedKey[0] ^= 0x01;

        using var other = new AesGcmEncryptor(tamperedKey);
        var ciphertext = _encryptor.EncryptString("secret");

        Assert.ThrowsAny<CryptographicException>(() => other.DecryptString(ciphertext));
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
                if (_encryptor.DecryptString(_encryptor.EncryptString(text)) != text)
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
