using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TicketApi.Auth;

/// <summary>Issues short-lived HMAC-SHA256 access tokens.</summary>
public sealed class TokenService(JwtOptions options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, int ExpiresIn) Create(IdentityUser user, IEnumerable<string> roles)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(options.LifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id,
                ["name"] = user.UserName ?? "",
                ["role"] = roles.ToArray(),
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(options.KeyBytes()), SecurityAlgorithms.HmacSha256),
        };
        return (_handler.CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }
}
