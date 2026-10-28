using CVManagement.Abstractions;
using CVManagement.DTOs;
using CVManagement.Entities;
using CVManagement.Results;

namespace CVManagement.Services;

public sealed class CandidateService : ICandidateService
{
    private static readonly HashSet<string> AllowedCvContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private readonly ICandidateRepository _candidates;
    private readonly IDegreeRepository _degrees;
    private readonly IUnitOfWork _unitOfWork;

    public CandidateService(ICandidateRepository candidates, IDegreeRepository degrees, IUnitOfWork unitOfWork)
    {
        _candidates = candidates;
        _degrees = degrees;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<CandidateDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
    {
        var items = await _candidates.GetAllAsync(page, pageSize, cancellationToken);
        var totalCount = await _candidates.CountAsync(cancellationToken);

        return new PagedResult<CandidateDto> { Items = items, TotalCount = totalCount };
    }

    public async Task<Result<CandidateDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.GetAsync(id, cancellationToken);

        return candidate is null
            ? Result<CandidateDto>.Fail(ServiceFailure.NotFound($"Candidate {id} was not found."))
            : Result<CandidateDto>.Ok(candidate);
    }

    public async Task<Result<CandidateDto>> CreateAsync(CandidateRequest request, CancellationToken cancellationToken = default)
    {
        var degreeFailure = await CheckDegreeAsync(request.DegreeId, cancellationToken);
        if (degreeFailure is not null)
        {
            return Result<CandidateDto>.Fail(degreeFailure);
        }

        var candidate = new Candidate
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Mobile = NormalizeMobile(request.Mobile),
            DegreeId = request.DegreeId
        };

        _candidates.Add(candidate);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Re-read through the single projection so the response always reflects persisted state.
        var created = await _candidates.GetAsync(candidate.Id, cancellationToken);

        return created is null
            ? Result<CandidateDto>.Fail(ServiceFailure.NotFound($"Candidate {candidate.Id} was not found after creation."))
            : Result<CandidateDto>.Ok(created);
    }

    public async Task<Result> UpdateAsync(int id, CandidateRequest request, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.FindAsync(id, cancellationToken);
        if (candidate is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"Candidate {id} was not found."));
        }

        var degreeFailure = await CheckDegreeAsync(request.DegreeId, cancellationToken);
        if (degreeFailure is not null)
        {
            return Result.Fail(degreeFailure);
        }

        candidate.FirstName = request.FirstName;
        candidate.LastName = request.LastName;
        candidate.Email = request.Email;
        candidate.Mobile = NormalizeMobile(request.Mobile);
        candidate.DegreeId = request.DegreeId;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.FindAsync(id, cancellationToken);
        if (candidate is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"Candidate {id} was not found."));
        }

        _candidates.Remove(candidate);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> UploadCvAsync(int id, CvUpload upload, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.FindAsync(id, cancellationToken);
        if (candidate is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"Candidate {id} was not found."));
        }

        if (upload.Content.Length == 0)
        {
            return Result.Fail(ServiceFailure.Validation("No file uploaded"));
        }

        if (!AllowedCvContentTypes.Contains(upload.ContentType))
        {
            return Result.Fail(ServiceFailure.Validation("Only PDF and Word documents are allowed"));
        }

        candidate.CVFile = upload.Content;
        candidate.CVFileName = upload.FileName;
        candidate.CVContentType = upload.ContentType;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<CvFileDto>> GetCvAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!await _candidates.ExistsAsync(id, cancellationToken))
        {
            return Result<CvFileDto>.Fail(ServiceFailure.NotFound($"Candidate {id} was not found."));
        }

        var cv = await _candidates.GetCvAsync(id, cancellationToken);

        return cv is null
            ? Result<CvFileDto>.Fail(ServiceFailure.NotFound("No CV found for this candidate"))
            : Result<CvFileDto>.Ok(cv);
    }

    public async Task<Result> DeleteCvAsync(int id, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.FindAsync(id, cancellationToken);
        if (candidate is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"Candidate {id} was not found."));
        }

        candidate.CVFile = null;
        candidate.CVFileName = null;
        candidate.CVContentType = null;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<ServiceFailure?> CheckDegreeAsync(int? degreeId, CancellationToken cancellationToken)
    {
        if (!degreeId.HasValue)
        {
            return null;
        }

        return await _degrees.ExistsAsync(degreeId.Value, cancellationToken)
            ? null
            : ServiceFailure.Validation("Invalid degree ID");
    }

    /// <summary>Collapses blank form input to null, keeping "not supplied" distinct from "supplied and wrong".</summary>
    private static string? NormalizeMobile(string? mobile)
        => string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim();
}
