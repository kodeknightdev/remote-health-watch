namespace RemoteHealthWatch.Contracts;

public enum VitalType
{
    HeartRate,
    SpO2,
    RespiratoryRate,
    Temperature,
    BloodPressure,
    Glucose
}

/// <summary>Canonical units after normalisation. Blood pressure carries diastolic in Value2.</summary>
public static class CanonicalUnits
{
    public static string For(VitalType vital) => vital switch
    {
        VitalType.HeartRate => "bpm",
        VitalType.SpO2 => "%",
        VitalType.RespiratoryRate => "/min",
        VitalType.Temperature => "C",
        VitalType.BloodPressure => "mmHg",
        VitalType.Glucose => "mg/dL",
        _ => throw new ArgumentOutOfRangeException(nameof(vital), vital, null)
    };
}
