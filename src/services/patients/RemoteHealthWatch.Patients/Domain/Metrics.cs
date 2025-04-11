namespace RemoteHealthWatch.Patients.Domain;

/// <summary>Threshold metrics. Blood pressure splits into systolic and diastolic.</summary>
public static class Metrics
{
    public const string HeartRate = "HeartRate";
    public const string SpO2 = "SpO2";
    public const string RespiratoryRate = "RespiratoryRate";
    public const string Temperature = "Temperature";
    public const string Systolic = "Systolic";
    public const string Diastolic = "Diastolic";
    public const string Glucose = "Glucose";

    public static readonly string[] All = [HeartRate, SpO2, RespiratoryRate, Temperature, Systolic, Diastolic, Glucose];
}

public static class Conditions
{
    public const string Copd = "COPD";
    public const string Hypertension = "Hypertension";
    public const string HeartFailure = "HeartFailure";
    public const string Diabetes = "Diabetes";
    public const string PostSurgical = "PostSurgical";
    public const string Pneumonia = "Pneumonia";

    public static readonly string[] Known = [Copd, Hypertension, HeartFailure, Diabetes, PostSurgical, Pneumonia];
}
