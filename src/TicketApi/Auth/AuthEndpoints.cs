using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace TicketApi.Auth;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string? UserName, string? Password);

    public sealed record LoginResponse(string AccessToken, int ExpiresIn);

    public sealed record MeResponse(string? UserName, IReadOnlyList<string> Roles);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");
        group.MapGet("/me", (ClaimsPrincipal user) => new MeResponse(
                user.FindFirstValue("name"), user.FindAll("role").Select(c => c.Value).ToList()))
            .RequireAuthorization();
        return app;
    }

    /// <summary>
    /// Every failure (unknown user, wrong password, locked out) returns the same 401, so the response doesn't
    /// reveal whether an account exists. Unknown users still pay for a password hash to keep timings similar.
    /// </summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, UserManager<IdentityUser> users, TokenService tokens)
    {
        var invalid = TypedResults.Problem(title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);
        if (string.IsNullOrEmpty(request.UserName) || string.IsNullOrEmpty(request.Password))
        {
            return invalid;
        }

        var user = await users.FindByNameAsync(request.UserName);
        if (user is null)
        {
            users.PasswordHasher.HashPassword(new IdentityUser(), request.Password);
            return invalid;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return invalid;
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return invalid;
        }

        await users.ResetAccessFailedCountAsync(user);
        var (token, expiresIn) = tokens.Create(user, await users.GetRolesAsync(user));
        return TypedResults.Ok(new LoginResponse(token, expiresIn));
    }
}
