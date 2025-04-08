using System.Security.Claims;
using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.ServiceDefaults.Auth;

public static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public static string PrimaryRole(this ClaimsPrincipal user) =>
        user.FindFirstValue("role") ?? "";

    public static bool SeesAllPatients(this ClaimsPrincipal user) =>
        Roles.ProgrammeWide.Any(user.IsInRole);
}
