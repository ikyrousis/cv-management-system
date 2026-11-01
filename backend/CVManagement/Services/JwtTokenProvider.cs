using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CVManagement.DTOs;
using CVManagement.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CVManagement.Services;

/// Issues short-lived HS256 tokens carrying the identity and the single role claim. Refresh
/// credentials are a separate concern with their own generator.
public sealed class JwtTokenProvider : ITokenProvider
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public JwtTokenProvider(IOptions<AuthOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public AccessTokenResponse IssueToken(User user)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            // Short-form claim: the bearer middleware maps it to ClaimTypes.Role on the principal.
            new Claim("role", user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(_options.SigningKeyBytes),
                SecurityAlgorithms.HmacSha256));

        return new AccessTokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires.UtcDateTime);
    }
}
