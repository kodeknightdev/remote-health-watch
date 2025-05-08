using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Alerts.Data;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Alerts.Controllers;

public sealed record AlertSummary(
    Guid Id, Guid PatientId, AlertType Type, AlertSeverity Severity, VitalType? Vital, string? Metric, double? Value, string Message,
    AlertStatus Status, int EscalationLevel, string AssignedRole, DateTimeOffset RaisedAt, DateTimeOffset LastOccurredAt, int OccurrenceCount,
    DateTimeOffset? NextEscalationAt, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt);

public sealed record AlertDetail(AlertSummary Alert, IReadOnlyList<AlertTimelineEntry> Timeline, IReadOnlyList<AlertNote> Notes,
    Guid? AcknowledgedBy, Guid? ResolvedBy, string? ResolutionReason);

public sealed record ResolveRequest(string Reason);
public sealed record NoteRequest(string Text);
public sealed record DailyCount(DateOnly Day, int Raised, int Critical, int Escalated);
public sealed record AlertStats(int Days, int Raised, int Acknowledged, int Escalated, double? AverageSecondsToAcknowledge,
    double EscalationRate, IReadOnlyList<DailyCount> PerDay);

public sealed class ResolveValidator : AbstractValidator<ResolveRequest>
{
    public ResolveValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
}

public sealed class NoteValidator : AbstractValidator<NoteRequest>
{
    public NoteValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(2000);
}

[ApiController]
[Route("api/alerts")]
[Authorize(Policy = Policies.Staff)]
public sealed class AlertsController(AlertsDb db, EscalationManager escalation, IPatientAccess access, TimeProvider clock) : ControllerBase
{
    /// <summary>The inbox: unacknowledged first, then by severity and age, limited to the caller's patients.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AlertSummary>>> Inbox([FromQuery] string status = "open", [FromQuery] Guid? patientId = null,
        [FromQuery] AlertSeverity? minSeverity = null, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var query = db.Alerts.AsNoTracking();
        query = status switch
        {
            "open" => query.Where(a => a.Status != AlertStatus.Resolved),
            "unacknowledged" => query.Where(a => a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Acknowledged),
            "resolved" => query.Where(a => a.Status == AlertStatus.Resolved),
            _ => query
        };
        if (patientId is not null) query = query.Where(a => a.PatientId == patientId);

        var visible = await access.VisiblePatientsAsync(User, ct);
        if (visible is not null) query = query.Where(a => visible.Contains(a.PatientId));

        var rows = await query.OrderByDescending(a => a.RaisedAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        return rows
            .Where(a => minSeverity is null || a.Severity >= minSeverity)
            .OrderByDescending(a => a.IsAwaitingAcknowledgement)
            .ThenByDescending(a => a.Severity)
            .ThenBy(a => a.RaisedAt)
            .Select(ToSummary).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlertDetail>> Get(Guid id, CancellationToken ct)
    {
        var alert = await db.Alerts.AsNoTracking().Include(a => a.Timeline).Include(a => a.Notes).AsSplitQuery()
            .SingleOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null) return NotFound();
        if (!await access.AuthorizeAsync(User, alert.PatientId, "read", "alert", ct)) return Forbidden();
        return ToDetail(alert);
    }

    [HttpGet("patient/{patientId:guid}")]
    [PatientData("alerts")]
    public async Task<ActionResult<IReadOnlyList<AlertSummary>>> ForPatient(Guid patientId, [FromQuery] bool openOnly = true, CancellationToken ct = default)
    {
        var query = db.Alerts.AsNoTracking().Where(a => a.PatientId == patientId);
        if (openOnly) query = query.Where(a => a.Status != AlertStatus.Resolved);
        var rows = await query.OrderByDescending(a => a.RaisedAt).Take(200).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<ActionResult<AlertDetail>> Acknowledge(Guid id, CancellationToken ct)
    {
        var patientId = await PatientOfAsync(id, ct);
        if (patientId is null) return NotFound();
        if (!await access.AuthorizeAsync(User, patientId.Value, "write", "alert.ack", ct)) return Forbidden();
        if (!await escalation.AcknowledgeAsync(id, User.UserId(), ct))
            return Problem("Alert is already acknowledged or resolved", statusCode: StatusCodes.Status409Conflict);
        return await Get(id, ct);
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<AlertDetail>> Resolve(Guid id, ResolveRequest request, CancellationToken ct)
    {
        var patientId = await PatientOfAsync(id, ct);
        if (patientId is null) return NotFound();
        if (!await access.AuthorizeAsync(User, patientId.Value, "write", "alert.resolve", ct)) return Forbidden();
        if (!await escalation.ResolveAsync(id, User.UserId(), request.Reason, ct))
            return Problem("Alert is already resolved", statusCode: StatusCodes.Status409Conflict);
        return await Get(id, ct);
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult<AlertNote>> AddNote(Guid id, NoteRequest request, CancellationToken ct)
    {
        var patientId = await PatientOfAsync(id, ct);
        if (patientId is null) return NotFound();
        if (!await access.AuthorizeAsync(User, patientId.Value, "write", "alert.note", ct)) return Forbidden();
        var now = clock.GetUtcNow();
        var note = new AlertNote
        {
            Id = Guid.NewGuid(),
            AlertId = id,
            AuthorId = User.UserId(),
            AuthorName = User.FindFirst("name")?.Value ?? "",
            Text = request.Text,
            At = now
        };
        db.Notes.Add(note);
        db.Timeline.Add(new AlertTimelineEntry { AlertId = id, At = now, Kind = TimelineKind.Note, ActorUserId = note.AuthorId, Detail = "note added" });
        await db.SaveChangesAsync(ct);
        return note;
    }

    [HttpGet("stats")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<AlertStats>> Stats([FromQuery] int days = 7, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var since = clock.GetUtcNow().AddDays(-days);
        var rows = await db.Alerts.AsNoTracking().Where(a => a.RaisedAt >= since)
            .Select(a => new { a.RaisedAt, a.Severity, a.AcknowledgedAt, a.EscalationLevel, a.Status }).ToListAsync(ct);
        var paged = rows.Where(r => r.Severity != AlertSeverity.Info).ToList();
        var acked = paged.Where(r => r.AcknowledgedAt is not null).ToList();
        var escalated = paged.Count(r => r.EscalationLevel > 0);
        var perDay = rows.GroupBy(r => DateOnly.FromDateTime(r.RaisedAt.UtcDateTime)).OrderBy(g => g.Key)
            .Select(g => new DailyCount(g.Key, g.Count(), g.Count(r => r.Severity == AlertSeverity.Critical), g.Count(r => r.EscalationLevel > 0)))
            .ToList();
        return new AlertStats(days, rows.Count, acked.Count, escalated,
            acked.Count == 0 ? null : Math.Round(acked.Average(r => (r.AcknowledgedAt!.Value - r.RaisedAt).TotalSeconds), 1),
            paged.Count == 0 ? 0 : Math.Round((double)escalated / paged.Count, 3), perDay);
    }

    private Task<Guid?> PatientOfAsync(Guid alertId, CancellationToken ct) =>
        db.Alerts.Where(a => a.Id == alertId).Select(a => (Guid?)a.PatientId).SingleOrDefaultAsync(ct);

    private ObjectResult Forbidden() => Problem("Patient is not on your care team", statusCode: StatusCodes.Status403Forbidden);

    internal static AlertSummary ToSummary(Alert a) => new(a.Id, a.PatientId, a.Type, a.Severity, a.Vital, a.Metric, a.Value, a.Message,
        a.Status, a.EscalationLevel, a.AssignedRole, a.RaisedAt, a.LastOccurredAt, a.OccurrenceCount, a.NextEscalationAt, a.AcknowledgedAt,
        a.ResolvedAt);

    private static AlertDetail ToDetail(Alert a) => new(ToSummary(a), a.Timeline.OrderBy(t => t.At).ThenBy(t => t.Id).ToList(),
        a.Notes.OrderBy(n => n.At).ToList(), a.AcknowledgedBy, a.ResolvedBy, a.ResolutionReason);
}
