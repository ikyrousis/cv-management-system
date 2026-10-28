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

        return await query.ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => _context.Candidates.CountAsync(cancellationToken);

    public async Task<CandidateDto?> GetAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Candidates
            .Where(c => c.Id == id)
            .Select(Projections.CandidateToDto)
            .FirstOrDefaultAsync(cancellationToken);

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

    public void Remove(Candidate candidate) => _context.Candidates.Remove(candidate);
}
