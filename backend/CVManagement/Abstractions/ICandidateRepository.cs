using CVManagement.DTOs;
using CVManagement.Entities;

namespace CVManagement.Abstractions;

public interface ICandidateRepository
{
    /// Projects straight into the read model; never materializes stored CV bytes.
    Task<IReadOnlyList<CandidateDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<CandidateDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// Loads the tracked entity for mutation; commit through IUnitOfWork.
    Task<Candidate?> FindAsync(int id, CancellationToken cancellationToken = default);

    Task<CvFileDto?> GetCvAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    void Add(Candidate candidate);

    /// Deletes from the key alone, so a row whose protected columns cannot be decrypted can still be removed.
    void RemoveById(int id);

    /// Clears the stored CV from the key alone, without reading the protected columns or the CV bytes.
    void DetachCvById(int id);
}
