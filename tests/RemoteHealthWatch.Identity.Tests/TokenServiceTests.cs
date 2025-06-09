using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RemoteHealthWatch.Identity.Data;
using RemoteHealthWatch.Identity.Domain;
using RemoteHealthWatch.Identity.Services;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Identity.Tests;

public class TokenServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2025, 4, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly IdentityDb _db = new(new DbContextOptionsBuilder<IdentityDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AppUser _nurse = new() { Id = Guid.NewGuid(), Email = "nurse@remotehealthwatch.local", DisplayName = "Sam", Role = "Nurse" };
    private readonly TokenService _tokens;

    public TokenServiceTests()
    {
        _db.Users.Add(_nurse);
        _db.SaveChanges();
        _tokens = new TokenService(_db, Options.Create(new JwtOptions { SigningKey = new string('k', 40), AccessTokenMinutes = 10, RefreshTokenDays = 7 }), _clock);
    }

    [Fact]
    public async Task Access_token_carries_role_and_surrogate_subject_only()
    {
        var issued = await _tokens.IssueAsync(_nurse, default);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        jwt.Subject.ShouldBe(_nurse.Id.ToString());
        jwt.Claims.Single(c => c.Type == "role").Value.ShouldBe("Nurse");
        jwt.ValidTo.ShouldBe(_clock.GetUtcNow().AddMinutes(10).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Refresh_token_is_stored_hashed()
    {
        var issued = await _tokens.IssueAsync(_nurse, default);
        var stored = await _db.RefreshTokens.SingleAsync();
        stored.TokenHash.ShouldNotBe(issued.RefreshToken);
        stored.TokenHash.ShouldBe(TokenService.Hash(issued.RefreshToken));
    }

    [Fact]
    public async Task Rotation_issues_a_new_token_and_retires_the_old_one()
    {
        var first = await _tokens.IssueAsync(_nurse, default);
        var (outcome, second, _) = await _tokens.RotateAsync(first.RefreshToken, default);
        outcome.ShouldBe(RefreshOutcome.Rotated);
        second!.RefreshToken.ShouldNotBe(first.RefreshToken);

        var (again, _, _) = await _tokens.RotateAsync(second.RefreshToken, default);
        again.ShouldBe(RefreshOutcome.Rotated);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_family()
    {
        var first = await _tokens.IssueAsync(_nurse, default);
        var (_, second, _) = await _tokens.RotateAsync(first.RefreshToken, default);

        // an attacker replays the stolen first token
        (await _tokens.RotateAsync(first.RefreshToken, default)).Outcome.ShouldBe(RefreshOutcome.ReuseDetected);
        // and the legitimate holder of the newer token is logged out too
        (await _tokens.RotateAsync(second!.RefreshToken, default)).Outcome.ShouldBe(RefreshOutcome.ReuseDetected);
        (await _db.RefreshTokens.AllAsync(t => t.RevokedAt != null)).ShouldBeTrue();
    }

    [Fact]
    public async Task Expired_refresh_token_is_refused()
    {
        var issued = await _tokens.IssueAsync(_nurse, default);
        _clock.Advance(TimeSpan.FromDays(8));
        (await _tokens.RotateAsync(issued.RefreshToken, default)).Outcome.ShouldBe(RefreshOutcome.Expired);
    }

    [Fact]
    public async Task Disabled_user_cannot_refresh()
    {
        var issued = await _tokens.IssueAsync(_nurse, default);
        _nurse.IsActive = false;
        await _db.SaveChangesAsync();
        (await _tokens.RotateAsync(issued.RefreshToken, default)).Outcome.ShouldBe(RefreshOutcome.UserDisabled);
    }

    [Fact]
    public async Task Logout_revokes_the_family()
    {
        var issued = await _tokens.IssueAsync(_nurse, default);
        await _tokens.RevokeAsync(issued.RefreshToken, default);
        (await _tokens.RotateAsync(issued.RefreshToken, default)).Outcome.ShouldBe(RefreshOutcome.ReuseDetected);
    }
}
