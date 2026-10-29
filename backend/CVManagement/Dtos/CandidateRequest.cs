using System.ComponentModel.DataAnnotations;
using CVManagement.Validation;

namespace CVManagement.DTOs;

public sealed class CandidateRequest
{
    [Required(ErrorMessage = "Last name is required")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "First name is required")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [RegularExpression(Patterns.Email, ErrorMessage = "Please enter a valid email address (emailname@domain.com)")]
    public string Email { get; set; } = string.Empty;

    [RegularExpression(Patterns.Mobile, ErrorMessage = "Mobile must be exactly 10 digits")]
    public string? Mobile { get; set; }

    public int? DegreeId { get; set; }
}
