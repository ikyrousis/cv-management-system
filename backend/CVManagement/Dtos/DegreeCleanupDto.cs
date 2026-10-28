namespace CVManagement.DTOs;

public sealed class DegreeCleanupDto
{
    public string Message { get; set; } = string.Empty;

    public int DeletedCount { get; set; }

    public IReadOnlyList<DegreeRefDto> DeletedDegrees { get; set; } = [];
}

public sealed class DegreeRefDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
