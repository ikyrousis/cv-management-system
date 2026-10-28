using CVManagement.Api.Extensions;
using CVManagement.Application.Dtos;
using CVManagement.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CVManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CandidatesController : ControllerBase
{
    private readonly ICandidateService _candidates;

    public CandidatesController(ICandidateService candidates) => _candidates = candidates;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CandidateDto>>> GetCandidates(CancellationToken cancellationToken)
        => Ok(await _candidates.GetAllAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CandidateDto>> GetCandidate(int id, CancellationToken cancellationToken)
        => this.AsResult(await _candidates.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CandidateDto>> CreateCandidate(
        CandidateRequest request,
        CancellationToken cancellationToken)
        => this.AsResult(
            await _candidates.CreateAsync(request, cancellationToken),
            created => CreatedAtAction(nameof(GetCandidate), new { id = created.Id }, created));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCandidate(
        int id,
        CandidateRequest request,
        CancellationToken cancellationToken)
        => this.AsNoContent(await _candidates.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCandidate(int id, CancellationToken cancellationToken)
        => this.AsNoContent(await _candidates.DeleteAsync(id, cancellationToken));

    [HttpPost("{id:int}/cv")]
    public async Task<IActionResult> UploadCV(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest("No file uploaded");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var upload = new CvUpload(buffer.ToArray(), file.FileName, file.ContentType);
        var result = await _candidates.UploadCvAsync(id, upload, cancellationToken);

        return result.IsSuccess
            ? Ok(new MessageDto("CV uploaded successfully"))
            : this.AsError(result.Failure);
    }

    [HttpGet("{id:int}/cv")]
    public async Task<IActionResult> DownloadCV(int id, CancellationToken cancellationToken)
    {
        var result = await _candidates.GetCvAsync(id, cancellationToken);

        if (result.Value is not { } cv)
        {
            return this.AsError(result.Failure);
        }

        return File(cv.Content, cv.ContentType ?? "application/octet-stream", cv.FileName);
    }

    [HttpDelete("{id:int}/cv")]
    public async Task<IActionResult> DeleteCV(int id, CancellationToken cancellationToken)
        => this.AsNoContent(await _candidates.DeleteCvAsync(id, cancellationToken));
}
