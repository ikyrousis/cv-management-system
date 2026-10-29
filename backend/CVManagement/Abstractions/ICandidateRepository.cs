using CVManagement.DTOs;
using CVManagement.Entities;

namespace CVManagement.Abstractions;

public interface ICandidateRepository
{
    /// Projects straight into the read model; never materializes stored CV bytes.
    Task<IReadOnlyList<CandidateDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<CandidateDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// Loads the tracked entity for mutation; commit through <see cref="IUnitOfWork"/>.
    Task<Candidate?> FindAsync(int id, CancellationToken cancellationToken = default);

    Task<CvFileDto?> GetCvAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    void Add(Candidate candidate);

    void Remove(Candidate candidate);
}
