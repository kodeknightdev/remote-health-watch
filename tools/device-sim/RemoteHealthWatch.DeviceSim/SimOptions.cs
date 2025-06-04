namespace RemoteHealthWatch.DeviceSim;

public sealed record SimOptions(
    string GatewayUrl,
    string DevicesFile,
    int Devices,
    int? IntervalSeconds,
    TimeSpan DeteriorationTime,
    TimeSpan DropoutCycle,
    TimeSpan DropoutLength)
{
    public static SimOptions FromEnvironment()
    {
        static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
        var url = Env("GATEWAY_URL", "http://localhost:5000/");
        return new SimOptions(
            url.EndsWith('/') ? url : url + "/",
            Env("SIM_DEVICES_FILE", "tools/seed/out/devices.json"),
            Math.Clamp(int.Parse(Env("SIM_DEVICES", "60")), 1, 1000),
            int.TryParse(Environment.GetEnvironmentVariable("SIM_INTERVAL_SECONDS"), out var i) ? i : null,
            TimeSpan.FromMinutes(double.Parse(Env("SIM_DETERIORATION_MINUTES", "30"), System.Globalization.CultureInfo.InvariantCulture)),
            TimeSpan.FromMinutes(double.Parse(Env("SIM_DROPOUT_CYCLE_MINUTES", "10"), System.Globalization.CultureInfo.InvariantCulture)),
            TimeSpan.FromMinutes(double.Parse(Env("SIM_DROPOUT_MINUTES", "3"), System.Globalization.CultureInfo.InvariantCulture)));
    }
}
