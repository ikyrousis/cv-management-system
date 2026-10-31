using System.Security.Cryptography;
using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace CVManagement.Tests.Unit;

/// The value converters capture the encryptor instance while the model is built, so the cached model
/// has to be keyed by that instance. Two contexts configured with different keys must not share one
/// model, otherwise every write silently uses whichever key built the model first.
public sealed class ModelCacheEncryptionTests
{
    private const string ConnectionString = "Server=nowhere;Database=none;Trusted_Connection=False";
    private const string ProbeEmail = "probe@example.com";

    [Fact]
    public void ContextsWithDifferentEncryptors_EachConverterUsesItsOwnKey()
    {
        using var firstEncryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        using var secondEncryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));

        using var firstContext = NewContext(firstEncryptor);
        using var secondContext = NewContext(secondEncryptor);

        Assert.NotSame(firstContext.Model, secondContext.Model);

        var stored = (string)EmailConverter(secondContext).ConvertToProvider(ProbeEmail)!;

        Assert.Equal(ProbeEmail, secondEncryptor.DecryptString(stored, "Candidate.Email"));
        Assert.ThrowsAny<CryptographicException>(() => firstEncryptor.DecryptString(stored, "Candidate.Email"));
    }

    [Fact]
    public void ContextsSharingOneEncryptor_ReuseTheCachedModel()
    {
        using var encryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));

        using var firstContext = NewContext(encryptor);
        using var secondContext = NewContext(encryptor);

        Assert.Same(firstContext.Model, secondContext.Model);
    }

    private static ApplicationDbContext NewContext(IEncryptor encryptor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseEncryptedSqlServer(ConnectionString);

        return new ApplicationDbContext(options.Options, encryptor);
    }

    private static ValueConverter EmailConverter(ApplicationDbContext context)
        => context.Model.FindEntityType(typeof(Candidate))!
            .FindProperty(nameof(Candidate.Email))!
            .GetValueConverter()!;
}
