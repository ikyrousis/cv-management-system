using CVManagement.Application.Dtos;
using CVManagement.Application.Entities;

namespace CVManagement.Application.Abstractions;

public interface ICandidateRepository
{
    /// <summary>
    /// Reads project straight into the read model, so stored CV bytes are never materialized
    /// for callers that only need metadata.
    /// </summary>
    Task<IReadOnlyList<CandidateDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CandidateDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked entity for mutation. Callers must commit through <see cref="IUnitOfWork"/>.
    /// </summary>
    Task<Candidate?> FindAsync(int id, CancellationToken cancellationToken = default);

    Task<CvFileDto?> GetCvAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    void Add(Candidate candidate);

    void Remove(Candidate candidate);
}
