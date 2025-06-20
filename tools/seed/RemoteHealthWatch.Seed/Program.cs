using System.Text.Json;
using RemoteHealthWatch.Seed;

// Creates the synthetic programme through the public APIs (so enrollment really goes through the saga)
// and writes the device keys the simulator needs. Safe to run again.
var gateway = Env("GATEWAY_URL", "http://localhost:5000/");
var email = Env("SEED_ADMIN_EMAIL", "coordinator@remotehealthwatch.local");
var password = Env("SEED_DEMO_PASSWORD", "");
var patientCount = int.Parse(Env("SEED_PATIENTS", "60"));
var spareDevices = int.Parse(Env("SEED_SPARE_DEVICES", "5"));
var interval = int.Parse(Env("SEED_INTERVAL_SECONDS", "5"));
var output = Env("SEED_OUT", "tools/seed/out/devices.json");

if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("SEED_DEMO_PASSWORD is not set");
    return 1;
}

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
var ct = cts.Token;
using var http = new HttpClient { BaseAddress = new Uri(gateway.EndsWith('/') ? gateway : gateway + "/"), Timeout = TimeSpan.FromSeconds(30) };
var api = new SeedClient(http);
await api.LoginAsync(email, password, ct);

var nurses = await api.StaffAsync("Nurse", ct);
var doctors = await api.StaffAsync("Doctor", ct);
var coordinators = await api.StaffAsync("CareCoordinator", ct);
if (nurses.Count == 0 || doctors.Count == 0) throw new InvalidOperationException("Demo staff missing; is identity running in Development?");
// demo accounts first so nurse@ and doctor@ get the first share of patients
nurses = nurses.OrderBy(n => n.Email.StartsWith("nurse@") ? 0 : 1).ThenBy(n => n.Email).ToList();
doctors = doctors.OrderBy(d => d.Email.StartsWith("doctor@") ? 0 : 1).ThenBy(d => d.Email).ToList();

var existingDevices = (await api.DevicesAsync(ct)).ToDictionary(d => d.Serial);
// keys from an earlier run are still valid; rotating them would cut off any simulator already using the file
var knownKeys = File.Exists(output)
    ? JsonSerializer.Deserialize<List<SeededDevice>>(await File.ReadAllTextAsync(output, ct), SeedClient.Json)!.ToDictionary(d => d.Serial)
    : new Dictionary<string, SeededDevice>();
var seeded = new List<SeededDevice>();
var rng = new Random(20250519);

for (var i = 0; i < patientCount; i++)
{
    var serial = $"RHW-PATCH-{i + 1:D4}";
    var scenario = ScenarioPlan.For(i);
    var conditions = ScenarioPlan.ConditionsFor(scenario, rng);
    var female = rng.NextDouble() < 0.55;
    var first = female ? Names.Female[rng.Next(Names.Female.Length)] : Names.Male[rng.Next(Names.Male.Length)];
    var last = Names.Last[rng.Next(Names.Last.Length)];
    var dob = new DateOnly(1938, 1, 1).AddDays(rng.Next(0, 365 * 45));

    if (existingDevices.TryGetValue(serial, out var known) && knownKeys.TryGetValue(serial, out var kept) && known.PatientId == kept.PatientId)
    {
        seeded.Add(kept);
        continue;
    }
    if (known is not null)
    {
        // already enrolled on an earlier run; a fresh key is the only way to get a usable secret back
        var rotated = await api.RotateKeyAsync(known.Id, ct);
        if (known.PatientId is { } pid) seeded.Add(new SeededDevice(known.Id, serial, rotated.ApiKey, pid, scenario.ToString(), interval));
        continue;
    }

    var device = await api.RegisterDeviceAsync(serial, "MultiParameterPatch", "2.4.1", ct);
    var nurse = nurses[i % 3 == 0 ? 0 : (i % nurses.Count)];
    var doctor = doctors[i % 2 == 0 ? 0 : (i % doctors.Count)];
    var careTeam = new List<object>
    {
        new { userId = nurse.Id, role = "Nurse", displayName = nurse.DisplayName, email = nurse.Email, phone = $"+44 7700 9{i:D5}", isPrimary = true },
        new { userId = doctor.Id, role = "Doctor", displayName = doctor.DisplayName, email = doctor.Email, phone = "+44 7700 900999", isPrimary = true }
    };
    if (coordinators.Count > 0)
    {
        var c = coordinators[0];
        careTeam.Add(new { userId = c.Id, role = "CareCoordinator", displayName = c.DisplayName, email = c.Email, phone = "", isPrimary = true });
    }

    var vitals = new List<string> { "HeartRate", "SpO2", "RespiratoryRate", "Temperature", "BloodPressure" };
    if (conditions.Contains("Diabetes")) vitals.Add("Glucose");
    var patientId = await api.EnrollAsync(new
    {
        firstName = first,
        lastName = last,
        dateOfBirth = dob.ToString("yyyy-MM-dd"),
        sex = female ? "F" : "M",
        conditions,
        plan = new { vitals, intervalSeconds = interval },
        careTeam,
        onSupplementalOxygen = scenario == Scenario.Copd && rng.NextDouble() < 0.3,
        deviceSerial = serial
    }, ct);
    seeded.Add(new SeededDevice(device.Device.Id, serial, device.ApiKey, patientId, scenario.ToString(), interval));
    Console.WriteLine($"enrolled {i + 1}/{patientCount} ({scenario})");
}

// a few unpaired devices for the registry screen, plus one enrollment naming a serial that doesn't exist,
// which exercises the pairing-failed compensation path
for (var i = 0; i < spareDevices; i++)
{
    var serial = $"RHW-SPARE-{i + 1:D3}";
    if (!existingDevices.ContainsKey(serial)) await api.RegisterDeviceAsync(serial, i % 2 == 0 ? "PulseOximeter" : "BloodPressureCuff", "1.9.0", ct);
}
if (!existingDevices.ContainsKey("RHW-PATCH-MISSING") && (await api.PatientsAsync(ct)).All(p => p.Name != "Orla Pending"))
{
    await api.EnrollAsync(new
    {
        firstName = "Orla",
        lastName = "Pending",
        dateOfBirth = "1950-02-14",
        sex = "F",
        conditions = new[] { "HeartFailure" },
        plan = new { vitals = new[] { "HeartRate", "SpO2", "BloodPressure" }, intervalSeconds = interval },
        careTeam = new[] { new { userId = nurses[0].Id, role = "Nurse", displayName = nurses[0].DisplayName, email = nurses[0].Email, phone = "", isPrimary = true } },
        onSupplementalOxygen = false,
        deviceSerial = "RHW-PATCH-MISSING"
    }, ct);
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(seeded, SeedClient.Json), ct);
Console.WriteLine($"wrote {seeded.Count} device keys to {output}");
return 0;

static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

public sealed record SeededDevice(Guid DeviceId, string Serial, string ApiKey, Guid PatientId, string Scenario, int IntervalSeconds);
