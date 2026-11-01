using System.ComponentModel.DataAnnotations;
using CVManagement.DTOs;
using CVManagement.Validation;
using Xunit;

namespace CVManagement.Tests.Unit;

/// The auth request contracts, same style as the candidate contracts: the rules are declared on the
/// DTOs, so they hold for every caller, and the username is checked after trimming.
public sealed class AuthContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Password_Empty_IsRejected(string? raw)
    {
        Assert.Contains("Password", FieldsWithErrors(Validate(new LoginRequest { Username = "admin", Password = raw! })));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("11-chars-ok")]
    public void Password_BelowMinimumLength_IsRejected(string raw)
    {
        Assert.Contains("Password", FieldsWithErrors(Validate(new LoginRequest { Username = "admin", Password = raw })));
    }

    [Theory]
    [InlineData("twelve-chars")]
    [InlineData("a long passphrase with spaces")]
    [InlineData("with trailing spaces kept intact ")]
    public void Password_AtOrAboveMinimumLength_IsAccepted(string raw)
    {
        // Spaces are legitimate password characters: the contract must accept them untrimmed.
        var request = new LoginRequest { Username = "admin", Password = raw };

        AssertEmpty(Validate(request));
        Assert.Equal(raw, request.Password);
    }

    [Theory]
    [InlineData("  admin  ", "admin")]
    [InlineData("alice", "alice")]
    public void Username_IsTrimmedBeforeValidation(string raw, string stored)
    {
        var request = new LoginRequest { Username = raw, Password = "valid-password-1" };

        AssertEmpty(Validate(request));
        Assert.Equal(stored, request.Username);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("bad username")]
    [InlineData("user@name")]
    [InlineData("usér")]
    public void Username_Invalid_IsRejected(string raw)
    {
        Assert.Contains("Username", FieldsWithErrors(Validate(new LoginRequest { Username = raw, Password = "valid-password-1" })));
    }

    [Fact]
    public void CreateUser_InvalidRole_FailsTheContract()
    {
        // The role is validated by the service with a clear message, but an empty value must still
        // fail the contract itself, so an unknown role can never arrive silently.
        Assert.Contains("Role", FieldsWithErrors(Validate(new CreateUserRequest { Username = "someone", Password = "valid-password-1" })));
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
