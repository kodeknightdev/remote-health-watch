using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Patients.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Policies.Admin)]
public sealed class AuditController(PatientsDb db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditEntry>>> Query([FromQuery] Guid? patientId, [FromQuery] Guid? userId,
        [FromQuery] bool? deniedOnly, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var query = db.Audit.AsNoTracking();
        if (patientId is not null) query = query.Where(a => a.PatientId == patientId);
        if (userId is not null) query = query.Where(a => a.UserId == userId);
        if (deniedOnly == true) query = query.Where(a => !a.Allowed);
        return await query.OrderByDescending(a => a.Id).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
    }
}
