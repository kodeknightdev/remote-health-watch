using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.Patients.Domain;

public static class CareTeamAccessPolicy
{
    /// <summary>Coordinators, admins and services see everyone; doctors and nurses only patients on their care team.</summary>
    public static bool CanAccess(Guid userId, string role, IEnumerable<CareTeamMember> careTeam)
    {
        if (Roles.ProgrammeWide.Contains(role)) return true;
        if (!Roles.Clinicians.Contains(role)) return false;
        return careTeam.Any(m => m.UserId == userId);
    }

    public static bool CanEditThresholds(Guid userId, string role, IEnumerable<CareTeamMember> careTeam) =>
        role != Roles.Service && CanAccess(userId, role, careTeam);
}
