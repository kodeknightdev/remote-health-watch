namespace RemoteHealthWatch.Contracts.Events;

/// <summary>Claim check: the PDF lives in blob storage, the event only points at it.</summary>
public sealed record ReportGenerated(Guid ReportId, Guid PatientId, DateOnly WeekStart, string Container, string BlobName, DateTimeOffset GeneratedAt)
    : IIntegrationEvent
{
    public static string EventType => "insights.report-generated";
    public static int SchemaVersion => 1;
}
