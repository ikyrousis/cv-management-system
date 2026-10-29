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
        var protectedValue = _encryptor.EncryptString("jane@example.com");

        Assert.DoesNotContain("@", protectedValue);
        Assert.Equal("jane@example.com", _encryptor.DecryptString(protectedValue));
    }

    [Fact]
    public void BytesRoundTrip_ReturnsOriginal()
    {
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 fake cv content");

        var protectedValue = _encryptor.EncryptBytes(content);

        Assert.NotEqual(content, protectedValue);
        Assert.Equal(content, _encryptor.DecryptBytes(protectedValue));
    }

    [Fact]
    public void SameInputTwice_ProducesDifferentCiphertext()
    {
        var first = _encryptor.EncryptString("same input");
        var second = _encryptor.EncryptString("same input");

        Assert.NotEqual(first, second);
        Assert.Equal(first.Length, second.Length);
    }

    [Fact]
    public void TamperedPayload_FailsTagCheck()
    {
        var payload = _encryptor.EncryptBytes(new byte[] { 1, 2, 3, 4 });
        payload[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.DecryptBytes(payload));
    }

    [Fact]
    public void WrongKey_CannotDecrypt()
    {
        using var other = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        var protectedValue = _encryptor.EncryptString("secret");

        Assert.ThrowsAny<CryptographicException>(() => other.DecryptString(protectedValue));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void InvalidKeyLength_Rejected(int length)
    {
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptor(new byte[length]));
    }
}
