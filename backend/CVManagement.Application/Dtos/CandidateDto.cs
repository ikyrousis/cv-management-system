namespace CVManagement.Application.Dtos;

/// <summary>
/// Read model returned to callers. Server-owned values only; nothing here is accepted as input.
/// </summary>
public sealed class CandidateDto
{
    public int Id { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Mobile { get; set; }

    public int? DegreeId { get; set; }

    public string? DegreeName { get; set; }

    public string? CVFileName { get; set; }

    public bool HasCV { get; set; }

    public DateTime CreationTime { get; set; }

    public string FullName { get; set; } = string.Empty;
}
