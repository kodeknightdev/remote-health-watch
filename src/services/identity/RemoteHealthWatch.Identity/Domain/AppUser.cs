using Microsoft.AspNetCore.Identity;

namespace RemoteHealthWatch.Identity.Domain;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";

    /// <summary>Doctors flagged on call receive escalations when the patient's own doctor isn't reachable.</summary>
    public bool IsOnCall { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";

    /// <summary>All tokens descending from one login share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByHash { get; set; }
}
