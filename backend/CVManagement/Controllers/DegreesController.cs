using CVManagement.Extensions;
using System.ComponentModel.DataAnnotations;
using CVManagement.DTOs;
using CVManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace CVManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DegreesController : ControllerBase
{
    private readonly IDegreeService _degrees;

    public DegreesController(IDegreeService degrees) => _degrees = degrees;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DegreeDto>>> GetDegrees(
        [FromQuery][Range(1, int.MaxValue)] int? page,
        [FromQuery][Range(1, 100)] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _degrees.GetAllAsync(page, pageSize, cancellationToken);

        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();

        return Ok(result.Items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DegreeDto>> GetDegree(int id, CancellationToken cancellationToken)
        => this.AsResult(await _degrees.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DegreeDto>> CreateDegree(DegreeRequest request, CancellationToken cancellationToken)
        => this.AsResult(
            await _degrees.CreateAsync(request, cancellationToken),
            created => CreatedAtAction(nameof(GetDegree), new { id = created.Id }, created));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDegree(int id, DegreeRequest request, CancellationToken cancellationToken)
        => this.AsNoContent(await _degrees.UpdateAsync(id, request, cancellationToken));

    /// <summary>Literal segment keeps this route distinct from <c>{id:int}</c> without relying on route precedence.</summary>
    [HttpDelete("cleanup")]
    public async Task<ActionResult<DegreeCleanupDto>> CleanupUnusedDegrees(CancellationToken cancellationToken)
        => Ok(await _degrees.CleanupUnassignedAsync(cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDegree(int id, CancellationToken cancellationToken)
        => this.AsNoContent(await _degrees.DeleteAsync(id, cancellationToken));
}
