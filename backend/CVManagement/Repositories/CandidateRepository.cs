using System.Security.Cryptography;
using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.DTOs;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Repositories;

internal sealed class CandidateRepository : ICandidateRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CandidateRepository> _logger;

    public CandidateRepository(ApplicationDbContext context, ILogger<CandidateRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CandidateDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
    {
        var query = Paging.Apply(
            _context.Candidates.OrderBy(c => c.Id).Select(Projections.CandidateToDto),
            page,
            pageSize);

        List<CandidateDto> items;

        try
        {
            items = await query.ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUnreadableData(ex))
        {
            // A page-level failure names no row, so the rows of this page are probed by key to find
            // the ones that are actually unreadable. Reached only on the failure path.
            var culprits = await FindUnreadableRowsAsync(page, pageSize, cancellationToken);

            foreach (var id in culprits)
            {
                _logger.LogError(ex, "Candidate {CandidateId} could not be decrypted", id);
            }

            throw new UnreadableStoredDataException(
                "Stored candidate data could not be decrypted.",
                culprits.Count > 0 ? culprits[0] : null,
                ex);
        }

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
        var candidate = await ReadAsync(id, cancellationToken, () => _context.Candidates
            .Where(c => c.Id == id)
            .Select(Projections.CandidateToDto)
            .FirstOrDefaultAsync(cancellationToken));

        if (candidate is not null)
        {
            candidate.FullName = $"{candidate.FirstName} {candidate.LastName}";
        }

        return candidate;
    }

    public Task<Candidate?> FindAsync(int id, CancellationToken cancellationToken = default)
        => ReadAsync(id, cancellationToken, () => _context.Candidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken));

    public Task<CvFileDto?> GetCvAsync(int id, CancellationToken cancellationToken = default)
        => ReadAsync(id, cancellationToken, () => _context.Candidates
            .Where(c => c.Id == id && c.CVFile != null)
            .Select(c => new CvFileDto
            {
                Content = c.CVFile!,
                FileName = c.CVFileName,
                ContentType = c.CVContentType
            })
            .FirstOrDefaultAsync(cancellationToken));

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

    /// Reads that touch protected columns report the row by numeric id, never by content.
    private async Task<T> ReadAsync<T>(int id, CancellationToken cancellationToken, Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (IsUnreadableData(ex))
        {
            _logger.LogError(ex, "Candidate {CandidateId} could not be decrypted", id);

            throw new UnreadableStoredDataException("Stored candidate data could not be decrypted.", id, ex);
        }
    }

    private async Task<IReadOnlyList<int>> FindUnreadableRowsAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var pageIds = await Paging.Apply(_context.Candidates.OrderBy(c => c.Id).Select(c => c.Id), page, pageSize)
            .ToListAsync(cancellationToken);

        var culprits = new List<int>();

        foreach (var id in pageIds)
        {
            try
            {
                await _context.Candidates
                    .Where(c => c.Id == id)
                    .Select(Projections.CandidateToDto)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex) when (IsUnreadableData(ex))
            {
                culprits.Add(id);
            }
        }

        return culprits;
    }

    private static bool IsUnreadableData(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is CryptographicException or FormatException)
            {
                return true;
            }
        }

        return false;
    }
}
