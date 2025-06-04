namespace RemoteHealthWatch.DeviceSim;

public enum Scenario
{
    Stable,
    Deteriorating,
    Copd,
    Hypertensive,
    NoisySensor,
    Dropout
}

public sealed record Vitals(double HeartRate, double SpO2, double RespiratoryRate, double Temperature, double Systolic, double Diastolic, double Glucose);

/// <summary>Per-patient baseline plus a scenario-specific drift. Deterministic for a given seed so runs are repeatable.</summary>
public sealed class Physiology
{
    private readonly Random _rng;
    private readonly Scenario _scenario;
    private readonly Vitals _baseline;
    private readonly TimeSpan _deteriorationTime;

    public Physiology(Scenario scenario, int seed, TimeSpan deteriorationTime)
    {
        _rng = new Random(seed);
        _scenario = scenario;
        _deteriorationTime = deteriorationTime;
        _baseline = scenario switch
        {
            // COPD: lives at 88-91% with a slightly fast resting respiratory rate
            Scenario.Copd => new Vitals(Between(70, 90), Between(88.5, 91), Between(18, 21), Between(36.5, 37.0), Between(120, 140), Between(72, 85), Between(95, 130)),
            Scenario.Hypertensive => new Vitals(Between(68, 85), Between(95, 98), Between(13, 17), Between(36.4, 37.0), Between(158, 172), Between(94, 102), Between(100, 160)),
            _ => new Vitals(Between(62, 84), Between(95.5, 98.5), Between(12, 17), Between(36.4, 37.1), Between(112, 135), Between(68, 84), Between(85, 140))
        };
    }

    public Vitals Sample(TimeSpan elapsed)
    {
        var b = _baseline;
        // slow circadian-ish wobble so trends aren't flat lines
        var wave = Math.Sin(elapsed.TotalMinutes / 45.0 * Math.PI);
        var v = new Vitals(
            b.HeartRate + 3 * wave + Noise(1.5),
            b.SpO2 + Noise(0.4),
            b.RespiratoryRate + 0.8 * wave + Noise(0.6),
            b.Temperature + 0.1 * wave + Noise(0.05),
            b.Systolic + 4 * wave + Noise(3),
            b.Diastolic + 2 * wave + Noise(2),
            b.Glucose + 15 * wave + Noise(5));

        if (_scenario == Scenario.Deteriorating)
        {
            // sepsis-like slide: tachycardia, tachypnoea, fever, falling saturation and pressure
            var p = Math.Clamp(elapsed / _deteriorationTime, 0, 1);
            v = v with
            {
                HeartRate = v.HeartRate + 45 * p,
                RespiratoryRate = v.RespiratoryRate + 12 * p,
                Temperature = v.Temperature + 2.4 * p,
                SpO2 = v.SpO2 - 7 * p,
                Systolic = v.Systolic - 35 * p,
                Diastolic = v.Diastolic - 18 * p
            };
        }
        else if (_scenario == Scenario.NoisySensor && _rng.NextDouble() < 0.04)
        {
            // motion artefact: one wild value, gone on the next sample
            v = _rng.Next(3) switch
            {
                0 => v with { HeartRate = Between(165, 190) },
                1 => v with { SpO2 = Between(72, 82) },
                _ => v with { RespiratoryRate = Between(32, 40) }
            };
        }

        return v with
        {
            SpO2 = Math.Min(100, v.SpO2),
            HeartRate = Math.Max(30, v.HeartRate),
            Diastolic = Math.Min(v.Diastolic, v.Systolic - 15)
        };
    }

    private double Between(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    private double Noise(double sd)
    {
        // Box-Muller
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        return sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
