using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.DTOs;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Repositories;

internal sealed class CandidateRepository : ICandidateRepository
{
    private readonly ApplicationDbContext _context;

    public CandidateRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<CandidateDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Candidates
            .OrderBy(c => c.Id)
            .Select(Projections.CandidateToDto);

        query = Paging.Apply(query, page, pageSize);

        var items = await query.ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.FullName = $"{item.FirstName} {item.LastName}";
        }

        return items;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => _context.Candidates.CountAsync(cancellationToken);

    public async Task<CandidateDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var candidate = await _context.Candidates
            .Where(c => c.Id == id)
            .Select(Projections.CandidateToDto)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is not null)
        {
            candidate.FullName = $"{candidate.FirstName} {candidate.LastName}";
        }

        return candidate;
    }

    public Task<Candidate?> FindAsync(int id, CancellationToken cancellationToken = default)
        => _context.Candidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CvFileDto?> GetCvAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Candidates
            .Where(c => c.Id == id && c.CVFile != null)
            .Select(c => new CvFileDto
            {
                Content = c.CVFile!,
                FileName = c.CVFileName,
                ContentType = c.CVContentType
            })
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
        => _context.Candidates.AnyAsync(c => c.Id == id, cancellationToken);

    public void Add(Candidate candidate) => _context.Candidates.Add(candidate);

    // The key alone identifies the row, so no protected column and no CV bytes are read to delete it.
    public void RemoveById(int id) => _context.Candidates.Remove(new Candidate { Id = id });

    public void DetachCvById(int id)
    {
        var stub = new Candidate { Id = id };
        _context.Candidates.Attach(stub);

        var entry = _context.Entry(stub);

        // Nothing differs from the attached snapshot, so the three CV columns are cleared explicitly.
        entry.Property(c => c.CVFile).IsModified = true;
        entry.Property(c => c.CVFileName).IsModified = true;
        entry.Property(c => c.CVContentType).IsModified = true;
    }
}
