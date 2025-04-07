namespace RemoteHealthWatch.Contracts.Events;

public static class Topics
{
    public const string RetrySuffix = ".retry";
    public const string DeadLetterSuffix = ".dlq";

    public const string ReadingsRaw = "devices.readings-raw";
    public const string ReadingsNormalized = "vitals.readings-normalized";

    public const string DevicePaired = "devices.device-paired";
    public const string DeviceUnpaired = "devices.device-unpaired";
    public const string DevicePairingFailed = "devices.device-pairing-failed";
    public const string DeviceSilent = "devices.device-silent";

    public const string PatientEnrolled = "patients.patient-enrolled";
    public const string ThresholdsChanged = "patients.thresholds-changed";
    public const string CareTeamChanged = "patients.care-team-changed";
    public const string PatientDischarged = "patients.patient-discharged";
    public const string MonitoringStatusChanged = "patients.monitoring-status-changed";

    public const string RiskScoreUpdated = "risk.score-updated";
    public const string RiskAnomalyDetected = "risk.anomaly-detected";
    public const string RiskDeteriorationTrend = "risk.deterioration-trend";

    public const string AlertRaised = "alerts.alert-raised";
    public const string AlertAcknowledged = "alerts.acknowledged";
    public const string AlertEscalated = "alerts.escalated";
    public const string AlertResolved = "alerts.resolved";

    public const string ReportGenerated = "insights.report-generated";
}
