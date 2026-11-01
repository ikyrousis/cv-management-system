using CVManagement.Extensions;
using CVManagement.DTOs;
using System.ComponentModel.DataAnnotations;
using CVManagement.Services;
using CVManagement.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CVManagement.Controllers;

/// Account management is the only surface with a role requirement.
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthLimits.AdminRole)]
public sealed class UsersController : ControllerBase
{
    private readonly IAuthService _auth;

    public UsersController(IAuthService auth) => _auth = auth;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers(
        [FromQuery][Range(1, int.MaxValue)] int? page,
        [FromQuery][Range(1, PagingLimits.MaxPageSize)] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _auth.GetAllAsync(page, pageSize, cancellationToken);

        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();

        return Ok(result.Items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.CreateUserAsync(request, cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.AsError(result.Failure);
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateUser(int id, CancellationToken cancellationToken)
        => this.AsNoContent(await _auth.DeactivateAsync(id, cancellationToken));
}
