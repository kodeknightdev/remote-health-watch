using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Patients.Domain;

public class Patient
{
    public Guid Id { get; set; }
    public string Mrn { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string Sex { get; set; } = "";
    public List<string> Conditions { get; set; } = [];
    public MonitoringStatus Status { get; set; }
    public MonitoringPlan Plan { get; set; } = new();

    /// <summary>NEWS2 SpO2 scale 2 is for confirmed hypercapnic respiratory failure (most of our COPD patients).</summary>
    public int SpO2Scale { get; set; } = 1;
    public bool OnSupplementalOxygen { get; set; }
    public string? RequestedDeviceSerial { get; set; }
    public int PairedDeviceCount { get; set; }
    public string? LastPairingError { get; set; }
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? BaselineStartedAt { get; set; }
    public DateTimeOffset? MonitoringActiveAt { get; set; }
    public DateTimeOffset? DischargedAt { get; set; }
    public Guid Version { get; set; }

    public List<CareTeamMember> CareTeam { get; set; } = [];
    public List<PatientThreshold> Thresholds { get; set; } = [];

    public int AgeOn(DateOnly today) =>
        today.Year - DateOfBirth.Year - (today < DateOfBirth.AddYears(today.Year - DateOfBirth.Year) ? 1 : 0);
}

public class MonitoringPlan
{
    public List<VitalType> Vitals { get; set; } = [];
    public int IntervalSeconds { get; set; } = 300;
}

public class CareTeamMember
{
    public Guid PatientId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public enum ThresholdSource
{
    Default,
    Condition,
    Override
}

public class PatientThreshold
{
    public Guid PatientId { get; set; }
    public string Metric { get; set; } = "";
    public double? LowCritical { get; set; }
    public double? LowWarning { get; set; }
    public double? HighWarning { get; set; }
    public double? HighCritical { get; set; }
    public ThresholdSource Source { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public Guid? PatientId { get; set; }
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Service { get; set; } = "";
    public bool Allowed { get; set; }
}
