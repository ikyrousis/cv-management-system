namespace CVManagement.Application.Dtos;

/// <summary>
/// Read model returned to callers.
/// </summary>
public sealed class DegreeDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public int CandidateCount { get; set; }
}
