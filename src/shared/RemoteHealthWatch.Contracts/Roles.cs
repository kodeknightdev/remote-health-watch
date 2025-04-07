namespace RemoteHealthWatch.Contracts;

public static class Roles
{
    public const string Doctor = "Doctor";
    public const string Nurse = "Nurse";
    public const string CareCoordinator = "CareCoordinator";
    public const string Admin = "Admin";

    // service-to-service calls minted by the gateway or batch jobs
    public const string Service = "Service";

    public static readonly string[] Staff = [Doctor, Nurse, CareCoordinator, Admin];
    public static readonly string[] Clinicians = [Doctor, Nurse];

    /// <summary>Roles that see every enrolled patient rather than only their care team.</summary>
    public static readonly string[] ProgrammeWide = [CareCoordinator, Admin, Service];
}
