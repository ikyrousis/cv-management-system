using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace CVManagement.Tests.Unit;

/// Pins what encrypting a column does to the SQL EF emits for predicates and ordering on that column.
/// These are descriptions of the current cost of the design, not checks of a desired feature.
public sealed class EncryptedQuerySemanticsTests
{
    private const string ConnectionString = "Server=nowhere;Database=none;Trusted_Connection=False";
    private const string ProbeEmail = "probe@example.com";

    [Fact]
    public void EmailEquality_EmitsFreshCiphertextAndNeverThePlaintext()
    {
        using var encryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        using var context = NewContext(encryptor);

        var sql = context.Candidates
            .Where(c => c.Email == ProbeEmail)
            .Select(c => c.Id)
            .ToQueryString();

        Assert.DoesNotContain(ProbeEmail, sql);

        var match = Regex.Match(sql, @"\[Email\] = N'(?<value>[^']+)'", RegexOptions.CultureInvariant);

        Assert.True(match.Success, sql);

        Assert.Equal(ProbeEmail, encryptor.DecryptString(match.Groups["value"].Value));
    }

    [Fact]
    public void EmailEquality_OperandCanNeverMatchStoredCiphertext()
    {
        using var encryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        var converter = EmailConverter(NewContext(encryptor));

        // The operand of an equality is itself encrypted with a fresh nonce, and the nonce is random,
        // so the value in the predicate differs from every value already stored for the same email.
        var first = (string)converter.ConvertToProvider(ProbeEmail)!;
        var second = (string)converter.ConvertToProvider(ProbeEmail)!;

        Assert.NotEqual(first, second);
        Assert.Equal(ProbeEmail, encryptor.DecryptString(first));
        Assert.Equal(ProbeEmail, encryptor.DecryptString(second));
    }

    [Fact]
    public void LastNameOrdering_SortsCiphertextNotNames()
    {
        using var encryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        using var context = NewContext(encryptor);

        var sql = context.Candidates.OrderBy(c => c.LastName).Select(c => c.Id).ToQueryString();

        // Ordering by the stored column orders base64 ciphertext, which is unrelated to name order,
        // so alphabetical sorting needs a plaintext sort key and cannot be pushed down as it stands.
        Assert.Contains("ORDER BY [c].[LastName]", sql);
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
