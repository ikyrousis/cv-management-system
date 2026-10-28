namespace CVManagement.Application.Entities;

public class Degree
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; } = DateTime.Now;

    public ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
}
