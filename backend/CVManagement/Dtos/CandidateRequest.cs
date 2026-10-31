using System.ComponentModel.DataAnnotations;
using CVManagement.Validation;

namespace CVManagement.DTOs;

public sealed class CandidateRequest
{
    private string _lastName = string.Empty;
    private string _firstName = string.Empty;
    private string _email = string.Empty;
    private string? _mobile;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
    public string LastName
    {
        get => _lastName;
        set => _lastName = value?.Trim() ?? string.Empty;
    }

    [Required(ErrorMessage = "First name is required")]
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
    public string FirstName
    {
        get => _firstName;
        set => _firstName = value?.Trim() ?? string.Empty;
    }

    [Required(ErrorMessage = "Email is required")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    [RegularExpression(Patterns.Email, ErrorMessage = "Please enter a valid email address (emailname@domain.com)")]
    public string Email
    {
        get => _email;
        set => _email = value?.Trim() ?? string.Empty;
    }

    /// Whitespace around a pasted number is formatting, not part of the value, so the digits are
    /// isolated before the pattern is checked.
    [RegularExpression(Patterns.Mobile, ErrorMessage = "Mobile must be exactly 10 digits")]
    public string? Mobile
    {
        get => _mobile;
        set => _mobile = string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    public int? DegreeId { get; set; }
}
