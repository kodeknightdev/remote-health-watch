using FluentValidation;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Domain;

namespace RemoteHealthWatch.Patients.Services;

public sealed record PlanDto(IReadOnlyList<VitalType> Vitals, int IntervalSeconds);
public sealed record CareTeamMemberInput(Guid UserId, string Role, string DisplayName, string Email, string? Phone, bool IsPrimary);
public sealed record ThresholdDto(string Metric, double? LowCritical, double? LowWarning, double? HighWarning, double? HighCritical, ThresholdSource Source);

public sealed record EnrollPatientRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Sex,
    IReadOnlyList<string> Conditions,
    PlanDto Plan,
    IReadOnlyList<CareTeamMemberInput> CareTeam,
    bool OnSupplementalOxygen = false,
    int? SpO2Scale = null,
    string? DeviceSerial = null,
    string? Mrn = null);

public sealed record UpdateThresholdsRequest(IReadOnlyList<ThresholdDto> Thresholds);
public sealed record UpdateCareTeamRequest(IReadOnlyList<CareTeamMemberInput> Members);
public sealed record UpdatePlanRequest(PlanDto Plan, bool OnSupplementalOxygen, int SpO2Scale);

public sealed record PatientSummary(
    Guid Id,
    string Mrn,
    string Name,
    int Age,
    string Sex,
    IReadOnlyList<string> Conditions,
    MonitoringStatus Status,
    DateTimeOffset EnrolledAt,
    string? PrimaryNurse,
    string? PrimaryDoctor);

public sealed record PatientDetail(
    Guid Id,
    string Mrn,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    int Age,
    string Sex,
    IReadOnlyList<string> Conditions,
    MonitoringStatus Status,
    PlanDto Plan,
    int SpO2Scale,
    bool OnSupplementalOxygen,
    int PairedDeviceCount,
    string? LastPairingError,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? BaselineStartedAt,
    DateTimeOffset? MonitoringActiveAt,
    DateTimeOffset? DischargedAt,
    IReadOnlyList<CareTeamMemberInput> CareTeam,
    IReadOnlyList<ThresholdDto> Thresholds);

public sealed class EnrollPatientValidator : AbstractValidator<EnrollPatientRequest>
{
    public EnrollPatientValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16))).WithMessage("Programme is adults only");
        RuleFor(x => x.Sex).Must(s => s is "F" or "M" or "X");
        RuleForEach(x => x.Conditions).Must(c => Conditions.Known.Contains(c)).WithMessage("Unknown condition '{PropertyValue}'");
        RuleFor(x => x.Plan).NotNull().SetValidator(new PlanValidator());
        RuleFor(x => x.CareTeam).NotEmpty();
        RuleForEach(x => x.CareTeam).SetValidator(new CareTeamMemberValidator());
        RuleFor(x => x.CareTeam).Must(t => t.Any(m => m.Role == Roles.Nurse)).WithMessage("Care team needs at least one nurse");
        RuleFor(x => x.SpO2Scale).Must(s => s is null or 1 or 2);
        RuleFor(x => x.DeviceSerial).MaximumLength(40);
    }
}

public sealed class PlanValidator : AbstractValidator<PlanDto>
{
    public PlanValidator()
    {
        RuleFor(x => x.Vitals).NotEmpty();
        RuleFor(x => x.IntervalSeconds).InclusiveBetween(5, 3600);
    }
}

public sealed class CareTeamMemberValidator : AbstractValidator<CareTeamMemberInput>
{
    public CareTeamMemberValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).Must(r => r is Roles.Doctor or Roles.Nurse or Roles.CareCoordinator);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed class UpdateCareTeamValidator : AbstractValidator<UpdateCareTeamRequest>
{
    public UpdateCareTeamValidator()
    {
        RuleFor(x => x.Members).NotEmpty();
        RuleForEach(x => x.Members).SetValidator(new CareTeamMemberValidator());
    }
}

public sealed class UpdatePlanValidator : AbstractValidator<UpdatePlanRequest>
{
    public UpdatePlanValidator()
    {
        RuleFor(x => x.Plan).NotNull().SetValidator(new PlanValidator());
        RuleFor(x => x.SpO2Scale).InclusiveBetween(1, 2);
    }
}

public sealed class UpdateThresholdsValidator : AbstractValidator<UpdateThresholdsRequest>
{
    public UpdateThresholdsValidator()
    {
        RuleFor(x => x.Thresholds).NotEmpty();
        RuleForEach(x => x.Thresholds).ChildRules(t =>
        {
            t.RuleFor(x => x.Metric).Must(m => Metrics.All.Contains(m)).WithMessage("Unknown metric '{PropertyValue}'");
            t.RuleFor(x => x).Must(BeOrdered).WithMessage("Thresholds must satisfy lowCritical <= lowWarning < highWarning <= highCritical");
        });
    }

    internal static bool BeOrdered(ThresholdDto t)
    {
        var ordered = new[] { t.LowCritical, t.LowWarning, t.HighWarning, t.HighCritical }.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i] < ordered[i - 1]) return false;
        return !(t.LowWarning.HasValue && t.HighWarning.HasValue && t.LowWarning >= t.HighWarning);
    }
}
