using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.Patients.Services;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Patients.Controllers;

public sealed record StatusCount(MonitoringStatus Status, int Count);

[ApiController]
[Route("api/patients")]
[Authorize(Policy = Policies.Staff)]
public sealed class PatientsController(PatientsDb db, PatientService patients, AuditLog audit, TimeProvider clock) : ControllerBase
{
    private const string Service = "patients";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatientSummary>>> List([FromQuery] MonitoringStatus? status,
        [FromQuery] string? search, CancellationToken ct)
    {
        var query = db.Patients.Include(p => p.CareTeam).AsNoTracking().AsQueryable();
        if (!User.SeesAllPatients())
        {
            var userId = User.UserId();
            query = query.Where(p => p.CareTeam.Any(m => m.UserId == userId));
        }
        if (status is not null) query = query.Where(p => p.Status == status);
        else query = query.Where(p => p.Status != MonitoringStatus.Discharged);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.LastName.StartsWith(search) || p.FirstName.StartsWith(search) || p.Mrn == search);

        var list = await query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(1000).ToListAsync(ct);
        await audit.WriteAsync(User.UserId(), User.PrimaryRole(), null, "list", $"patients:{list.Count}", Service, true, ct);
        var today = Today();
        return list.Select(p => PatientService.ToSummary(p, today)).ToList();
    }

    [HttpGet("{patientId:guid}")]
    public async Task<ActionResult<PatientDetail>> Get(Guid patientId, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return NotFound();
        if (!await AuthorizeAsync(patient, "read", "patient", ct)) return Forbidden();
        return PatientService.ToDetail(patient, Today());
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<PatientDetail>> Enroll(EnrollPatientRequest request, CancellationToken ct)
    {
        if (request.Mrn is not null && await db.Patients.AnyAsync(p => p.Mrn == request.Mrn, ct))
            return Problem("MRN already enrolled", statusCode: StatusCodes.Status409Conflict);
        var patient = await patients.EnrollAsync(request, ct);
        await audit.WriteAsync(User.UserId(), User.PrimaryRole(), patient.Id, "create", "patient", Service, true, ct);
        return CreatedAtAction(nameof(Get), new { patientId = patient.Id }, PatientService.ToDetail(patient, Today()));
    }

    [HttpGet("{patientId:guid}/thresholds")]
    public async Task<ActionResult<IReadOnlyList<ThresholdDto>>> Thresholds(Guid patientId, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return NotFound();
        if (!await AuthorizeAsync(patient, "read", "thresholds", ct)) return Forbidden();
        return PatientService.ToDetail(patient, Today()).Thresholds.ToList();
    }

    [HttpPut("{patientId:guid}/thresholds")]
    public async Task<ActionResult<IReadOnlyList<ThresholdDto>>> UpdateThresholds(Guid patientId, UpdateThresholdsRequest request, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return NotFound();
        var allowed = CareTeamAccessPolicy.CanEditThresholds(User.UserId(), User.PrimaryRole(), patient.CareTeam);
        audit.Record(User.UserId(), User.PrimaryRole(), patient.Id, "write", "thresholds", Service, allowed);
        if (!allowed)
        {
            await db.SaveChangesAsync(ct);
            return Forbidden();
        }
        await patients.UpdateThresholdsAsync(patient, request.Thresholds, User.UserId(), ct);
        return PatientService.ToDetail(patient, Today()).Thresholds.ToList();
    }

    [HttpDelete("{patientId:guid}/thresholds/{metric}")]
    public async Task<IActionResult> ResetThreshold(Guid patientId, string metric, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null || !Metrics.All.Contains(metric)) return NotFound();
        if (!await AuthorizeAsync(patient, "write", "thresholds", ct)) return Forbidden();
        await patients.ResetThresholdAsync(patient, metric, ct);
        return NoContent();
    }

    [HttpPut("{patientId:guid}/plan")]
    public async Task<ActionResult<PatientDetail>> UpdatePlan(Guid patientId, UpdatePlanRequest request, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return NotFound();
        if (!CareTeamAccessPolicy.CanEditThresholds(User.UserId(), User.PrimaryRole(), patient.CareTeam)) return Forbidden();
        audit.Record(User.UserId(), User.PrimaryRole(), patient.Id, "write", "plan", Service, true);
        await patients.UpdatePlanAsync(patient, request, ct);
        return PatientService.ToDetail(patient, Today());
    }

    [HttpPut("{patientId:guid}/care-team")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<PatientDetail>> UpdateCareTeam(Guid patientId, UpdateCareTeamRequest request, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return NotFound();
        audit.Record(User.UserId(), User.PrimaryRole(), patient.Id, "write", "care-team", Service, true);
        await patients.UpdateCareTeamAsync(patient, request.Members, ct);
        return PatientService.ToDetail(patient, Today());
    }

    [HttpPost("{patientId:guid}/discharge")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<IActionResult> Discharge(Guid patientId, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return NotFound();
        audit.Record(User.UserId(), User.PrimaryRole(), patient.Id, "write", "discharge", Service, true);
        if (!await patients.DischargeAsync(patient, ct))
            return Problem("Patient already discharged", statusCode: StatusCodes.Status409Conflict);
        return NoContent();
    }

    [HttpGet("stats")]
    public async Task<ActionResult<IReadOnlyList<StatusCount>>> Stats(CancellationToken ct)
    {
        var counts = await db.Patients.GroupBy(p => p.Status).Select(g => new StatusCount(g.Key, g.Count())).ToListAsync(ct);
        return counts;
    }

    private async Task<bool> AuthorizeAsync(Patient patient, string action, string resource, CancellationToken ct)
    {
        var allowed = CareTeamAccessPolicy.CanAccess(User.UserId(), User.PrimaryRole(), patient.CareTeam);
        await audit.WriteAsync(User.UserId(), User.PrimaryRole(), patient.Id, action, resource, Service, allowed, ct);
        return allowed;
    }

    private ObjectResult Forbidden() => Problem("Patient is not on your care team", statusCode: StatusCodes.Status403Forbidden);

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
