using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RemoteHealthWatch.Identity.Domain;
using RemoteHealthWatch.Identity.Services;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Identity.Controllers;

public sealed record LoginRequest(string Email, string Password);
public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, bool IsOnCall);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<AppUser> users, TokenService tokens, ILogger<AuthController> logger) : ControllerBase
{
    public const string RefreshCookie = "rhw_refresh";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return InvalidLogin();
        if (await users.IsLockedOutAsync(user)) return Problem("Account temporarily locked", statusCode: StatusCodes.Status423Locked);

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            logger.LogInformation("Failed login for user {UserId}", user.Id);
            return InvalidLogin();
        }
        await users.ResetAccessFailedCountAsync(user);

        var issued = await tokens.IssueAsync(user, ct);
        SetRefreshCookie(issued);
        return new TokenResponse(issued.AccessToken, issued.AccessExpiresAt, ToDto(user));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var raw) || string.IsNullOrEmpty(raw))
            return Unauthorized();

        var (outcome, issued, user) = await tokens.RotateAsync(raw, ct);
        if (outcome != RefreshOutcome.Rotated)
        {
            if (outcome == RefreshOutcome.ReuseDetected)
                logger.LogWarning("Refresh token reuse detected, token family revoked");
            Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
            return Unauthorized();
        }
        SetRefreshCookie(issued!);
        return new TokenResponse(issued!.AccessToken, issued.AccessExpiresAt, ToDto(user!));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrEmpty(raw))
            await tokens.RevokeAsync(raw, ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await users.FindByIdAsync(User.UserId().ToString());
        return user is null ? NotFound() : ToDto(user);
    }

    internal static UserDto ToDto(AppUser u) => new(u.Id, u.Email ?? "", u.DisplayName, u.Role, u.IsOnCall);

    private ActionResult InvalidLogin() => Problem("Invalid email or password", statusCode: StatusCodes.Status401Unauthorized);

    private void SetRefreshCookie(IssuedTokens issued) =>
        Response.Cookies.Append(RefreshCookie, issued.RefreshToken, CookieOptions(issued.RefreshExpiresAt));

    // scoped to the auth path so the cookie never rides along on data requests
    private static CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires
    };
}
