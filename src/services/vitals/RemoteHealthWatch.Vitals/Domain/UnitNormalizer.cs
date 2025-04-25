using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.Vitals.Domain;

public sealed record Normalized(double Value, double? Value2, string Unit);

/// <summary>Converts device units to canonical ones (C, mg/dL, mmHg, %) and rejects physiologically impossible values.</summary>
public static class UnitNormalizer
{
    private const double KPaToMmHg = 7.50062;
    private const double GlucoseMmolToMgDl = 18.0156;

    public static Normalized? Normalize(VitalType vital, double value, double? value2, string unit)
    {
        var normalized = (vital, unit) switch
        {
            (VitalType.Temperature, "F") => new Normalized(Math.Round((value - 32) * 5 / 9, 2), null, "C"),
            (VitalType.Glucose, "mmol/L") => new Normalized(Math.Round(value * GlucoseMmolToMgDl, 1), null, "mg/dL"),
            (VitalType.BloodPressure, "kPa") => new Normalized(Math.Round(value * KPaToMmHg), value2 is null ? null : Math.Round(value2.Value * KPaToMmHg), "mmHg"),
            // some oximeters report 0.97 rather than 97
            (VitalType.SpO2, "fraction") => new Normalized(Math.Round(value * 100, 1), null, "%"),
            (VitalType.SpO2, "%") when value <= 1.0 => new Normalized(Math.Round(value * 100, 1), null, "%"),
            (VitalType.RespiratoryRate, "brpm") => new Normalized(value, null, "/min"),
            _ when unit == CanonicalUnits.For(vital) => new Normalized(value, vital == VitalType.BloodPressure ? value2 : null, unit),
            _ => null
        };
        return normalized is not null && IsPlausible(vital, normalized) ? normalized : null;
    }

    internal static bool IsPlausible(VitalType vital, Normalized n) => vital switch
    {
        VitalType.HeartRate => n.Value is >= 20 and <= 250,
        VitalType.SpO2 => n.Value is >= 50 and <= 100,
        VitalType.RespiratoryRate => n.Value is >= 2 and <= 70,
        VitalType.Temperature => n.Value is >= 30 and <= 44,
        VitalType.BloodPressure => n.Value is >= 50 and <= 280 && n.Value2 is >= 20 and <= 180 && n.Value2 < n.Value,
        VitalType.Glucose => n.Value is >= 15 and <= 800,
        _ => false
    };
}
