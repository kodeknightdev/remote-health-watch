using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.Patients.Services;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Patients.Controllers;

public sealed record PairingEligibility(bool Eligible, MonitoringStatus? Status, string? Reason);
public sealed record AccessCheckRequest(Guid UserId, string Role, Guid PatientId, string Action, string Resource, string Service);
public sealed record AccessCheckResponse(bool Allowed);
public sealed record MonitoringProfileDto(Guid PatientId, MonitoringStatus Status, IReadOnlyList<string> Conditions, int SpO2Scale,
    bool OnSupplementalOxygen, int IntervalSeconds);
public sealed record VisiblePatients(bool All, IReadOnlyList<Guid> PatientIds);

/// <summary>REST lookups for services that don't speak gRPC (devices pairing, the Python services).</summary>
[ApiController]
[Route("internal")]
[Authorize]
public sealed class InternalController(PatientService patients, AuditLog audit, PatientsDb db) : ControllerBase
{
    [HttpGet("patients/{patientId:guid}/profile")]
    [Authorize(Policy = Policies.Internal)]
    public async Task<ActionResult<MonitoringProfileDto>> Profile(Guid patientId, CancellationToken ct)
    {
        var p = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return NotFound();
        return new MonitoringProfileDto(p.Id, p.Status, p.Conditions, p.SpO2Scale, p.OnSupplementalOxygen, p.Plan.IntervalSeconds);
    }

    [HttpGet("users/{userId:guid}/patients")]
    [Authorize(Policy = Policies.Internal)]
    public async Task<ActionResult<VisiblePatients>> Visible(Guid userId, [FromQuery] string role, CancellationToken ct)
    {
        if (Roles.ProgrammeWide.Contains(role)) return new VisiblePatients(true, []);
        var ids = await db.CareTeamMembers.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.PatientId).ToListAsync(ct);
        return new VisiblePatients(false, ids);
    }

    [HttpGet("patients/{patientId:guid}/pairing-eligibility")]
    public async Task<ActionResult<PairingEligibility>> PairingEligibility(Guid patientId, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return new PairingEligibility(false, null, "unknown-patient");
        if (!User.SeesAllPatients() && !CareTeamAccessPolicy.CanAccess(User.UserId(), User.PrimaryRole(), patient.CareTeam))
            return Forbid();
        return patient.Status == MonitoringStatus.Discharged
            ? new PairingEligibility(false, patient.Status, "discharged")
            : new PairingEligibility(true, patient.Status, null);
    }

    [HttpPost("access")]
    [Authorize(Policy = Policies.Internal)]
    public async Task<ActionResult<AccessCheckResponse>> Access(AccessCheckRequest request, CancellationToken ct)
    {
        var patient = await patients.FindAsync(request.PatientId, ct);
        var allowed = patient is not null && CareTeamAccessPolicy.CanAccess(request.UserId, request.Role, patient.CareTeam);
        await audit.WriteAsync(request.UserId, request.Role, request.PatientId, request.Action, request.Resource, request.Service, allowed, ct);
        return new AccessCheckResponse(allowed);
    }
}
