using CVManagement.Application.Abstractions;
using CVManagement.Application.Dtos;
using CVManagement.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Infrastructure.Persistence;

public sealed class DegreeRepository : IDegreeRepository
{
    private readonly ApplicationDbContext _context;

    public DegreeRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<DegreeDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.Degrees
            .Select(Projections.DegreeToDto)
            .ToListAsync(cancellationToken);

    public async Task<DegreeDto?> GetAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Degrees
            .Where(d => d.Id == id)
            .Select(Projections.DegreeToDto)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Degree?> FindAsync(int id, CancellationToken cancellationToken = default)
        => _context.Degrees.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
        => _context.Degrees.AnyAsync(d => d.Id == id, cancellationToken);

    public Task<bool> NameTakenAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = name.ToLower();

        var query = _context.Degrees.Where(d => d.Name.ToLower() == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(d => d.Id != excludeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> IsAssignedToCandidatesAsync(int id, CancellationToken cancellationToken = default)
        => _context.Candidates.AnyAsync(c => c.DegreeId == id, cancellationToken);

    public async Task<IReadOnlyList<Degree>> GetUnassignedAsync(CancellationToken cancellationToken = default)
        => await _context.Degrees
            .Where(d => !d.Candidates.Any())
            .ToListAsync(cancellationToken);

    public void Add(Degree degree) => _context.Degrees.Add(degree);

    public void Remove(Degree degree) => _context.Degrees.Remove(degree);

    public void RemoveRange(IEnumerable<Degree> degrees) => _context.Degrees.RemoveRange(degrees);
}
