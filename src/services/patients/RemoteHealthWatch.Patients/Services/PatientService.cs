using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.Patients.Services;

public sealed class PatientService(PatientsDb db, IEventOutbox outbox, TimeProvider clock)
{
    public Task<Patient?> FindAsync(Guid id, CancellationToken ct) =>
        db.Patients.Include(p => p.CareTeam).Include(p => p.Thresholds).AsSplitQuery().SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Patient> EnrollAsync(EnrollPatientRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var conditions = request.Conditions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            Mrn = request.Mrn ?? await NextMrnAsync(ct),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Sex = request.Sex,
            Conditions = conditions,
            Status = MonitoringStatus.PendingDevice,
            Plan = new MonitoringPlan { Vitals = request.Plan.Vitals.Distinct().ToList(), IntervalSeconds = request.Plan.IntervalSeconds },
            SpO2Scale = request.SpO2Scale ?? (conditions.Contains(Conditions.Copd, StringComparer.OrdinalIgnoreCase) ? 2 : 1),
            OnSupplementalOxygen = request.OnSupplementalOxygen,
            RequestedDeviceSerial = string.IsNullOrWhiteSpace(request.DeviceSerial) ? null : request.DeviceSerial.Trim(),
            EnrolledAt = now,
            Version = Guid.NewGuid()
        };
        patient.CareTeam = request.CareTeam.Select(m => ToMember(patient.Id, m)).ToList();
        patient.Thresholds = DefaultThresholds.For(conditions)
            .Select(kv => new PatientThreshold
            {
                PatientId = patient.Id,
                Metric = kv.Key,
                LowCritical = kv.Value.Values.LowCritical,
                LowWarning = kv.Value.Values.LowWarning,
                HighWarning = kv.Value.Values.HighWarning,
                HighCritical = kv.Value.Values.HighCritical,
                Source = kv.Value.Source,
                UpdatedAt = now
            }).ToList();

        db.Patients.Add(patient);
        outbox.Enqueue(Topics.PatientEnrolled, patient.Id, new PatientEnrolled(patient.Id, conditions,
            new MonitoringPlanDto(patient.Plan.Vitals, patient.Plan.IntervalSeconds), patient.SpO2Scale, patient.OnSupplementalOxygen,
            patient.RequestedDeviceSerial, now));
        outbox.Enqueue(Topics.CareTeamChanged, patient.Id, CareTeamEvent(patient, now));
        await db.SaveChangesAsync(ct);
        return patient;
    }

    public async Task UpdateThresholdsAsync(Patient patient, IReadOnlyList<ThresholdDto> thresholds, Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var input in thresholds)
        {
            var existing = patient.Thresholds.SingleOrDefault(t => t.Metric == input.Metric);
            if (existing is null)
            {
                existing = new PatientThreshold { PatientId = patient.Id, Metric = input.Metric };
                patient.Thresholds.Add(existing);
            }
            existing.LowCritical = input.LowCritical;
            existing.LowWarning = input.LowWarning;
            existing.HighWarning = input.HighWarning;
            existing.HighCritical = input.HighCritical;
            existing.Source = ThresholdSource.Override;
            existing.UpdatedBy = userId;
            existing.UpdatedAt = now;
        }
        patient.Version = Guid.NewGuid();
        outbox.Enqueue(Topics.ThresholdsChanged, patient.Id, new ThresholdsChanged(patient.Id, now));
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetThresholdAsync(Patient patient, string metric, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var defaults = DefaultThresholds.For(patient.Conditions);
        if (!defaults.TryGetValue(metric, out var d)) return;
        var existing = patient.Thresholds.Single(t => t.Metric == metric);
        (existing.LowCritical, existing.LowWarning, existing.HighWarning, existing.HighCritical) =
            (d.Values.LowCritical, d.Values.LowWarning, d.Values.HighWarning, d.Values.HighCritical);
        existing.Source = d.Source;
        existing.UpdatedBy = null;
        existing.UpdatedAt = now;
        patient.Version = Guid.NewGuid();
        outbox.Enqueue(Topics.ThresholdsChanged, patient.Id, new ThresholdsChanged(patient.Id, now));
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateCareTeamAsync(Patient patient, IReadOnlyList<CareTeamMemberInput> members, CancellationToken ct)
    {
        db.CareTeamMembers.RemoveRange(patient.CareTeam);
        patient.CareTeam = members.Select(m => ToMember(patient.Id, m)).ToList();
        db.CareTeamMembers.AddRange(patient.CareTeam);
        patient.Version = Guid.NewGuid();
        outbox.Enqueue(Topics.CareTeamChanged, patient.Id, CareTeamEvent(patient, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePlanAsync(Patient patient, UpdatePlanRequest request, CancellationToken ct)
    {
        patient.Plan = new MonitoringPlan { Vitals = request.Plan.Vitals.Distinct().ToList(), IntervalSeconds = request.Plan.IntervalSeconds };
        patient.OnSupplementalOxygen = request.OnSupplementalOxygen;
        patient.SpO2Scale = request.SpO2Scale;
        patient.Version = Guid.NewGuid();
        // risk-engine reads scale and oxygen from the profile, so this counts as a threshold change for consumers
        outbox.Enqueue(Topics.ThresholdsChanged, patient.Id, new ThresholdsChanged(patient.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DischargeAsync(Patient patient, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var transition = EnrollmentStateMachine.Discharge(patient, now);
        if (transition is null) return false;
        patient.Version = Guid.NewGuid();
        outbox.Enqueue(Topics.PatientDischarged, patient.Id, new PatientDischarged(patient.Id, now));
        PublishTransition(patient, transition);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public void PublishTransition(Patient patient, StatusTransition transition) =>
        outbox.Enqueue(Topics.MonitoringStatusChanged, patient.Id,
            new MonitoringStatusChanged(patient.Id, transition.From, transition.To, transition.Reason, clock.GetUtcNow()));

    private static CareTeamChanged CareTeamEvent(Patient patient, DateTimeOffset now) =>
        new(patient.Id, patient.CareTeam.Select(m => new CareTeamMemberDto(m.UserId, m.Role)).ToList(), now);

    private static CareTeamMember ToMember(Guid patientId, CareTeamMemberInput m) => new()
    {
        PatientId = patientId,
        UserId = m.UserId,
        Role = m.Role,
        DisplayName = m.DisplayName,
        Email = m.Email,
        Phone = m.Phone ?? "",
        IsPrimary = m.IsPrimary
    };

    private async Task<string> NextMrnAsync(CancellationToken ct)
    {
        var next = 100001 + await db.Patients.CountAsync(ct);
        while (await db.Patients.AnyAsync(p => p.Mrn == $"MRN-{next:D6}", ct)) next++;
        return $"MRN-{next:D6}";
    }

    public static PatientSummary ToSummary(Patient p, DateOnly today) => new(
        p.Id, p.Mrn, $"{p.FirstName} {p.LastName}", p.AgeOn(today), p.Sex, p.Conditions, p.Status, p.EnrolledAt,
        p.CareTeam.Where(m => m.Role == Roles.Nurse).OrderByDescending(m => m.IsPrimary).Select(m => m.DisplayName).FirstOrDefault(),
        p.CareTeam.Where(m => m.Role == Roles.Doctor).OrderByDescending(m => m.IsPrimary).Select(m => m.DisplayName).FirstOrDefault());

    public static PatientDetail ToDetail(Patient p, DateOnly today) => new(
        p.Id, p.Mrn, p.FirstName, p.LastName, p.DateOfBirth, p.AgeOn(today), p.Sex, p.Conditions, p.Status,
        new PlanDto(p.Plan.Vitals, p.Plan.IntervalSeconds), p.SpO2Scale, p.OnSupplementalOxygen, p.PairedDeviceCount, p.LastPairingError,
        p.EnrolledAt, p.BaselineStartedAt, p.MonitoringActiveAt, p.DischargedAt,
        p.CareTeam.Select(m => new CareTeamMemberInput(m.UserId, m.Role, m.DisplayName, m.Email, m.Phone, m.IsPrimary)).ToList(),
        p.Thresholds.OrderBy(t => Array.IndexOf(Metrics.All, t.Metric))
            .Select(t => new ThresholdDto(t.Metric, t.LowCritical, t.LowWarning, t.HighWarning, t.HighCritical, t.Source)).ToList());
}
