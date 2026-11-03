using System.Security.Claims;
using CVManagement.Extensions;
using CVManagement.DTOs;
using CVManagement.Services;
using CVManagement.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CVManagement.Controllers;

/// Anonymous by design: login and first-run bootstrap are the only unauthenticated entry points,
/// which also makes them the only endpoints worth throttling.
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicy.Auth)]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => this.AsResult(await _auth.LoginAsync(request, cancellationToken));

    /// Anonymous by design: the access token is expected to be expired or absent here, so only
    /// the refresh credential authenticates the exchange.
    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
        => this.AsResult(await _auth.RefreshAsync(request, cancellationToken));

    /// Requires a live access token: logout is the holder ending their own session, so it must not
    /// burn the anonymous throttle budget that guards the guessing surface. The account is taken
    /// from the authenticated principal, never from the request body, so a caller can only revoke
    /// a credential that is already theirs.
    [HttpPost("logout")]
    [Authorize]
    [DisableRateLimiting]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return this.AsNoContent(await _auth.RevokeAsync(callerId, request.RefreshToken, cancellationToken));
    }

    /// Exists only while the Users table is empty; the service closes it after the first account.
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(BootstrapRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.BootstrapAsync(request, cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.AsError(result.Failure);
    }

    /// The account the access token was issued to. The bearer middleware maps the short-form sub
    /// claim onto NameIdentifier, the same inbound mapping the role-based authorization depends on,
    /// so a principal without it names no account and no session can be scoped to it.
    private bool TryGetCallerId(out int callerId)
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out callerId);
}
