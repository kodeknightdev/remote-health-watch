using System.Diagnostics;
using System.Text.Json;
using RemoteHealthWatch.DeviceSim;

var options = SimOptions.FromEnvironment();
for (var i = 0; !File.Exists(options.DevicesFile); i++)
{
    if (i == 0) Console.WriteLine($"waiting for {options.DevicesFile} (run the seed first)");
    await Task.Delay(3000);
}

var devices = JsonSerializer.Deserialize<List<SeededDevice>>(await File.ReadAllTextAsync(options.DevicesFile), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
if (options.Devices > devices.Count)
    Console.WriteLine($"SIM_DEVICES={options.Devices} but only {devices.Count} seeded; raise SEED_PATIENTS to simulate more");
var fleet = devices.Take(options.Devices).ToList();

var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 256, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
using var http = new HttpClient(handler) { BaseAddress = new Uri(options.GatewayUrl), Timeout = TimeSpan.FromSeconds(10) };
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

var stats = new SimStats();
Console.WriteLine($"simulating {fleet.Count} devices every {options.IntervalSeconds?.ToString() ?? "plan"}s against {options.GatewayUrl}; " +
                  string.Join(", ", fleet.GroupBy(d => d.Scenario).Select(g => $"{g.Key} {g.Count()}")));

var runners = fleet.Select(d => new DeviceRunner(d, http, options, stats).RunAsync(cts.Token)).ToList();
var report = Task.Run(async () =>
{
    var clock = Stopwatch.StartNew();
    long lastAccepted = 0;
    var lastTime = TimeSpan.Zero;
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ContinueWith(_ => { });
        var accepted = Interlocked.Read(ref stats.Accepted);
        var elapsed = clock.Elapsed;
        var rate = (accepted - lastAccepted) / (elapsed - lastTime).TotalSeconds;
        Console.WriteLine($"[sim] {rate:0.0} readings/s accepted | total sent {stats.Sent} accepted {accepted} " +
                          $"rejected {stats.Rejected} throttled {stats.Throttled} failed {stats.Failed}");
        (lastAccepted, lastTime) = (accepted, elapsed);
    }
});

try
{
    await Task.WhenAll(runners);
}
catch (OperationCanceledException)
{
}
return 0;
