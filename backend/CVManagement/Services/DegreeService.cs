using CVManagement.Abstractions;
using CVManagement.Dtos;
using CVManagement.Entities;
using CVManagement.Results;

namespace CVManagement.Services;

public sealed class DegreeService : IDegreeService
{
    private readonly IDegreeRepository _degrees;
    private readonly IUnitOfWork _unitOfWork;

    public DegreeService(IDegreeRepository degrees, IUnitOfWork unitOfWork)
    {
        _degrees = degrees;
        _unitOfWork = unitOfWork;
    }

    public Task<IReadOnlyList<DegreeDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => _degrees.GetAllAsync(cancellationToken);

    public async Task<Result<DegreeDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var degree = await _degrees.GetAsync(id, cancellationToken);

        return degree is null
            ? Result<DegreeDto>.Fail(ServiceFailure.NotFound($"Degree {id} was not found."))
            : Result<DegreeDto>.Ok(degree);
    }

    public async Task<Result<DegreeDto>> CreateAsync(DegreeRequest request, CancellationToken cancellationToken = default)
    {
        if (await _degrees.NameTakenAsync(request.Name, excludeId: null, cancellationToken))
        {
            return Result<DegreeDto>.Fail(ServiceFailure.Validation("A degree with this name already exists"));
        }

        var degree = new Degree { Name = request.Name };

        _degrees.Add(degree);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Re-read through the single projection so the response always reflects persisted state.
        var created = await _degrees.GetAsync(degree.Id, cancellationToken);

        return created is null
            ? Result<DegreeDto>.Fail(ServiceFailure.NotFound($"Degree {degree.Id} was not found after creation."))
            : Result<DegreeDto>.Ok(created);
    }

    public async Task<Result> UpdateAsync(int id, DegreeRequest request, CancellationToken cancellationToken = default)
    {
        var degree = await _degrees.FindAsync(id, cancellationToken);
        if (degree is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"Degree {id} was not found."));
        }

        if (await _degrees.NameTakenAsync(request.Name, excludeId: id, cancellationToken))
        {
            return Result.Fail(ServiceFailure.Validation("A degree with this name already exists"));
        }

        degree.Name = request.Name;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var degree = await _degrees.FindAsync(id, cancellationToken);
        if (degree is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"Degree {id} was not found."));
        }

        if (await _degrees.IsAssignedToCandidatesAsync(id, cancellationToken))
        {
            return Result.Fail(ServiceFailure.Validation("Cannot delete degree that is assigned to candidates"));
        }

        _degrees.Remove(degree);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<DegreeCleanupDto> CleanupUnassignedAsync(CancellationToken cancellationToken = default)
    {
        var unassigned = await _degrees.GetUnassignedAsync(cancellationToken);

        if (unassigned.Count == 0)
        {
            return new DegreeCleanupDto
            {
                Message = "No unused degrees found",
                DeletedCount = 0
            };
        }

        _degrees.RemoveRange(unassigned);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DegreeCleanupDto
        {
            Message = $"Successfully deleted {unassigned.Count} unused degrees",
            DeletedCount = unassigned.Count,
            DeletedDegrees = unassigned
                .Select(d => new DegreeRefDto { Id = d.Id, Name = d.Name })
                .ToList()
        };
    }
}
