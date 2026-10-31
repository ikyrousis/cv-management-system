using System.ComponentModel.DataAnnotations;

namespace CVManagement.DTOs;

public sealed class DegreeRequest
{
    private string _name = string.Empty;

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name
    {
        get => _name;
        set => _name = value?.Trim() ?? string.Empty;
    }
}
