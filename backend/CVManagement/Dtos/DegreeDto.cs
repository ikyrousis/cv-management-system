namespace CVManagement.DTOs;

public sealed class DegreeDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public int CandidateCount { get; set; }
}
