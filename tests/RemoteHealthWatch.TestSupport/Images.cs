namespace RemoteHealthWatch.TestSupport;

/// <summary>Same pinned images docker-compose uses.</summary>
public static class Images
{
    public const string SqlServer = "mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04";
    public const string Redis = "redis:7.4.2-alpine";
}
