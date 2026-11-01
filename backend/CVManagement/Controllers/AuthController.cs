using CVManagement.Extensions;
using CVManagement.DTOs;
using CVManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace CVManagement.Controllers;

/// Anonymous by design: login and first-run bootstrap are the only unauthenticated entry points.
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => this.AsResult(await _auth.LoginAsync(request, cancellationToken));

    /// Exists only while the Users table is empty; the service closes it after the first account.
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(BootstrapRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.BootstrapAsync(request, cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.AsError(result.Failure);
    }
}
