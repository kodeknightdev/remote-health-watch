namespace RemoteHealthWatch.TestSupport;

/// <summary>Integration test that needs a Docker daemon for Testcontainers; skipped (not failed) without one.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailable()) Skip = "Docker is not available; Testcontainers integration test skipped";
    }

    private static bool DockerAvailable()
    {
        if (Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS") == "1") return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))) return true;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return File.Exists("/var/run/docker.sock") || File.Exists(Path.Combine(home, ".docker", "run", "docker.sock"));
    }
}
