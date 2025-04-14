using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;

namespace RemoteHealthWatch.Patients.Services;

public sealed class AuditLog(PatientsDb db, TimeProvider clock)
{
    /// <summary>Stages an audit row; written with the caller's SaveChanges.</summary>
    public void Record(Guid userId, string role, Guid? patientId, string action, string resource, string service, bool allowed) =>
        db.Audit.Add(new AuditEntry
        {
            At = clock.GetUtcNow(),
            UserId = userId,
            Role = role,
            PatientId = patientId,
            Action = action,
            Resource = resource,
            Service = service,
            Allowed = allowed
        });

    public async Task WriteAsync(Guid userId, string role, Guid? patientId, string action, string resource, string service, bool allowed,
        CancellationToken ct)
    {
        Record(userId, role, patientId, action, resource, service, allowed);
        await db.SaveChangesAsync(ct);
    }
}
