using CVManagement.Application.Dtos;
using CVManagement.Application.Results;

namespace CVManagement.Application.Services;

public interface IDegreeService
{
    Task<IReadOnlyList<DegreeDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<DegreeDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<DegreeDto>> CreateAsync(DegreeRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(int id, DegreeRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<DegreeCleanupDto> CleanupUnassignedAsync(CancellationToken cancellationToken = default);
}
