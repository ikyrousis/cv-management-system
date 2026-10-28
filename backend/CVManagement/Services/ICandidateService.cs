using CVManagement.Dtos;
using CVManagement.Results;

namespace CVManagement.Services;

public interface ICandidateService
{
    Task<IReadOnlyList<CandidateDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<CandidateDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<CandidateDto>> CreateAsync(CandidateRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(int id, CandidateRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> UploadCvAsync(int id, CvUpload upload, CancellationToken cancellationToken = default);

    Task<Result<CvFileDto>> GetCvAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> DeleteCvAsync(int id, CancellationToken cancellationToken = default);
}
