namespace CVManagement.Entities;

public class Candidate
{
    public int Id { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Mobile { get; set; }

    public int? DegreeId { get; set; }

    public byte[]? CVFile { get; set; }

    public string? CVFileName { get; set; }

    public string? CVContentType { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public Degree? Degree { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}
