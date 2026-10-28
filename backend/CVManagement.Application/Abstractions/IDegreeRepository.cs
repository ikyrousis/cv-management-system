using CVManagement.Application.Dtos;
using CVManagement.Application.Entities;

namespace CVManagement.Application.Abstractions;

public interface IDegreeRepository
{
    Task<IReadOnlyList<DegreeDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<DegreeDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked entity for mutation. Callers must commit through <see cref="IUnitOfWork"/>.
    /// </summary>
    Task<Degree?> FindAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive name lookup, optionally excluding one degree so an update can keep its own name.
    /// </summary>
    Task<bool> NameTakenAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedToCandidatesAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Degree>> GetUnassignedAsync(CancellationToken cancellationToken = default);

    void Add(Degree degree);

    void Remove(Degree degree);

    void RemoveRange(IEnumerable<Degree> degrees);
}
