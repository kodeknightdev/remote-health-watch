using FluentValidation;
using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.Devices.Ingestion;

public sealed record IngestReading(long Seq, VitalType Vital, double Value, double? Value2, string Unit, DateTimeOffset MeasuredAt);
public sealed record IngestBatch(int? BatteryPercent, IReadOnlyList<IngestReading> Readings);
public sealed record IngestAccepted(Guid BatchId, int Accepted);

public sealed class IngestBatchValidator : AbstractValidator<IngestBatch>
{
    public const int MaxReadings = 500;

    // units a device may send; vitals normalises them
    internal static readonly Dictionary<VitalType, string[]> AllowedUnits = new()
    {
        [VitalType.HeartRate] = ["bpm"],
        [VitalType.SpO2] = ["%", "fraction"],
        [VitalType.RespiratoryRate] = ["/min", "brpm"],
        [VitalType.Temperature] = ["C", "F"],
        [VitalType.BloodPressure] = ["mmHg", "kPa"],
        [VitalType.Glucose] = ["mg/dL", "mmol/L"]
    };

    public IngestBatchValidator(TimeProvider clock)
    {
        RuleFor(x => x.BatteryPercent).InclusiveBetween(0, 100).When(x => x.BatteryPercent is not null);
        RuleFor(x => x.Readings).NotEmpty().Must(r => r.Count <= MaxReadings).WithMessage($"At most {MaxReadings} readings per batch");
        RuleForEach(x => x.Readings).ChildRules(r =>
        {
            r.RuleFor(x => x.Seq).GreaterThan(0);
            r.RuleFor(x => x.Vital).IsInEnum();
            r.RuleFor(x => x.Unit).Must((reading, unit) => AllowedUnits.TryGetValue(reading.Vital, out var units) && units.Contains(unit))
                .WithMessage("Unit '{PropertyValue}' is not accepted for this vital");
            r.RuleFor(x => x.Value).Must(v => double.IsFinite(v) && v >= 0);
            r.RuleFor(x => x.Value2).NotNull().When(x => x.Vital == VitalType.BloodPressure).WithMessage("Blood pressure needs a diastolic value");
            // device clocks drift, but not by minutes
            r.RuleFor(x => x.MeasuredAt).Must(t => t <= clock.GetUtcNow().AddMinutes(2)).WithMessage("Reading is in the future");
            r.RuleFor(x => x.MeasuredAt).Must(t => t >= clock.GetUtcNow().AddDays(-7)).WithMessage("Reading is older than 7 days");
        });
        RuleFor(x => x.Readings).Must(r => r.Select(x => x.Seq).Distinct().Count() == r.Count)
            .WithMessage("Sequence numbers must be unique within a batch");
    }
}
