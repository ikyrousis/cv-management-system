using CVManagement.DTOs;
using CVManagement.Entities;

namespace CVManagement.Abstractions;

public interface IDegreeRepository
{
    Task<IReadOnlyList<DegreeDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<DegreeDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// Loads the tracked entity for mutation; commit through IUnitOfWork.
    Task<Degree?> FindAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// Case-insensitive name lookup, optionally excluding one degree.
    Task<bool> NameTakenAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedToCandidatesAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Degree>> GetUnassignedAsync(CancellationToken cancellationToken = default);

    void Add(Degree degree);

    void Remove(Degree degree);

    void RemoveRange(IEnumerable<Degree> degrees);
}
