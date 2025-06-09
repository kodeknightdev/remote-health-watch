using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Patients.Domain;

namespace RemoteHealthWatch.Patients.Tests;

public class CareTeamAccessTests
{
    private static readonly Guid Nurse = Guid.NewGuid();
    private static readonly Guid Doctor = Guid.NewGuid();
    private static readonly Guid OtherNurse = Guid.NewGuid();

    private static readonly CareTeamMember[] Team =
    [
        new() { UserId = Nurse, Role = Roles.Nurse },
        new() { UserId = Doctor, Role = Roles.Doctor }
    ];

    [Fact]
    public void Care_team_clinicians_can_see_their_patient()
    {
        CareTeamAccessPolicy.CanAccess(Nurse, Roles.Nurse, Team).ShouldBeTrue();
        CareTeamAccessPolicy.CanAccess(Doctor, Roles.Doctor, Team).ShouldBeTrue();
    }

    [Fact]
    public void Clinicians_outside_the_care_team_cannot()
    {
        CareTeamAccessPolicy.CanAccess(OtherNurse, Roles.Nurse, Team).ShouldBeFalse();
        CareTeamAccessPolicy.CanAccess(OtherNurse, Roles.Doctor, Team).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Roles.CareCoordinator)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Service)]
    public void Programme_wide_roles_see_everyone(string role) =>
        CareTeamAccessPolicy.CanAccess(Guid.NewGuid(), role, Team).ShouldBeTrue();

    [Fact]
    public void Unknown_role_is_denied_even_if_listed()
    {
        CareTeamAccessPolicy.CanAccess(Nurse, "Visitor", Team).ShouldBeFalse();
    }

    [Fact]
    public void Services_can_read_but_not_edit_thresholds()
    {
        CareTeamAccessPolicy.CanEditThresholds(Guid.Empty, Roles.Service, Team).ShouldBeFalse();
        CareTeamAccessPolicy.CanEditThresholds(Doctor, Roles.Doctor, Team).ShouldBeTrue();
        CareTeamAccessPolicy.CanEditThresholds(OtherNurse, Roles.Nurse, Team).ShouldBeFalse();
    }
}
