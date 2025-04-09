using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RemoteHealthWatch.Identity.Data;
using RemoteHealthWatch.Identity.Domain;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Identity.Services;

public sealed record IssuedTokens(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

public enum RefreshOutcome
{
    Rotated,
    NotFound,
    Expired,
    ReuseDetected,
    UserDisabled
}

public sealed class TokenService(IdentityDb db, IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<IssuedTokens> IssueAsync(AppUser user, CancellationToken ct)
    {
        var tokens = CreatePair(user, familyId: Guid.NewGuid(), out var refresh);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        return tokens;
    }

    /// <summary>Single-use rotation. Presenting an already-rotated token means it leaked, so the whole family goes.</summary>
    public async Task<(RefreshOutcome Outcome, IssuedTokens? Tokens, AppUser? User)> RotateAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null) return (RefreshOutcome.NotFound, null, null);

        var now = clock.GetUtcNow();
        if (current.RevokedAt is not null)
        {
            await RevokeFamilyAsync(current.FamilyId, "reuse-detected", ct);
            return (RefreshOutcome.ReuseDetected, null, null);
        }
        if (current.ExpiresAt <= now) return (RefreshOutcome.Expired, null, null);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(current.FamilyId, "user-disabled", ct);
            return (RefreshOutcome.UserDisabled, null, null);
        }

        var tokens = CreatePair(user, current.FamilyId, out var next);
        current.RevokedAt = now;
        current.RevokedReason = "rotated";
        current.ReplacedByHash = next.TokenHash;
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);
        return (RefreshOutcome.Rotated, tokens, user);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null) await RevokeFamilyAsync(token.FamilyId, "logout", ct);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt = now;
            t.RevokedReason = reason;
        }
        await db.SaveChangesAsync(ct);
    }

    private IssuedTokens CreatePair(AppUser user, Guid familyId, out RefreshToken refresh)
    {
        var now = clock.GetUtcNow();
        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", user.Role),
            new("name", user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email ?? "")
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now.UtcDateTime, accessExpires.UtcDateTime, credentials);
        var access = new JwtSecurityTokenHandler().WriteToken(jwt);

        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(raw),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays)
        };
        return new IssuedTokens(access, accessExpires, raw, refresh.ExpiresAt);
    }

    internal static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
