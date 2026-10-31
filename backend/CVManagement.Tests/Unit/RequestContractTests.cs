using System.ComponentModel.DataAnnotations;
using CVManagement.DTOs;
using CVManagement.Validation;
using Xunit;

namespace CVManagement.Tests.Unit;

/// The request contracts are the only place inbound rules are declared, and the values they check are
/// the trimmed ones, so a padded paste cannot pass a pattern the stored value would then violate.
public sealed class RequestContractTests
{
    [Theory]
    [InlineData(" 1234567890 ")]
    [InlineData("1234567890")]
    public void Mobile_AcceptsDigitsAroundWhitespace_AndStoresThemTrimmed(string raw)
    {
        var request = new CandidateRequest { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com", Mobile = raw };

        AssertEmpty(Validate(request));
        Assert.Equal("1234567890", request.Mobile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Mobile_AbsentIsNormalizedToNull(string? raw)
    {
        var request = new CandidateRequest { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com", Mobile = raw };

        AssertEmpty(Validate(request));
        Assert.Null(request.Mobile);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("+33612345678")]
    [InlineData("abc")]
    public void Mobile_RejectsAnythingThatIsNotTenDigits(string raw)
    {
        var request = new CandidateRequest { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com", Mobile = raw };

        Assert.Contains("Mobile", FieldsWithErrors(Validate(request)));
    }

    [Fact]
    public void NamesAndEmail_AreTrimmedBeforeRulesAreChecked()
    {
        var request = new CandidateRequest
        {
            FirstName = "  Ada  ",
            LastName = " Lovelace ",
            Email = " ada@example.com "
        };

        AssertEmpty(Validate(request));
        Assert.Equal("Ada", request.FirstName);
        Assert.Equal("Lovelace", request.LastName);
        Assert.Equal("ada@example.com", request.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Names_ThatAreBlankOrOnlyWhitespace_FailAsRequired(string blank)
    {
        var request = new CandidateRequest { FirstName = blank, LastName = "Lovelace", Email = "ada@example.com" };

        Assert.Contains("FirstName", FieldsWithErrors(Validate(request)));
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void Names_AreCappedServerSideAtFiftyCharacters(int length, bool expected)
    {
        var request = new CandidateRequest
        {
            FirstName = new string('a', length),
            LastName = "Lovelace",
            Email = "ada@example.com"
        };

        Assert.Equal(expected, Validate(request).Count == 0);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Email_IsCappedServerSideAtOneHundredCharacters(int length, bool expected)
    {
        var request = new CandidateRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"{new string('a', length - 12)}@example.com"
        };

        Assert.Equal(expected, Validate(request).Count == 0);
    }

    [Fact]
    public void ValidCandidateRequest_HasNoErrors()
    {
        var request = new CandidateRequest { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com", Mobile = "1234567890", DegreeId = 2 };

        AssertEmpty(Validate(request));
    }

    [Fact]
    public void DegreeName_IsTrimmedAndCapped()
    {
        var shortName = new DegreeRequest { Name = "  BSc Computing  " };
        AssertEmpty(Validate(shortName));
        Assert.Equal("BSc Computing", shortName.Name);

        var longName = new DegreeRequest { Name = new string('d', 101) };
        Assert.Contains("Name", FieldsWithErrors(Validate(longName)));
    }

    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        return results;
    }

    private static void AssertEmpty(IList<ValidationResult> results)
        => Assert.Empty(results);

    private static IEnumerable<string> FieldsWithErrors(IList<ValidationResult> results)
        => results.SelectMany(r => r.MemberNames);
}
