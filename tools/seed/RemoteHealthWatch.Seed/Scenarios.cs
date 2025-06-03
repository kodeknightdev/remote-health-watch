namespace RemoteHealthWatch.Seed;

public enum Scenario
{
    Stable,
    Deteriorating,
    Copd,
    Hypertensive,
    NoisySensor,
    Dropout
}

public static class ScenarioPlan
{
    // repeating block of ten so every nurse's third of the list gets a mix, including a deteriorating patient
    private static readonly Scenario[] Block =
    [
        Scenario.Deteriorating, Scenario.Stable, Scenario.Copd, Scenario.Stable, Scenario.Hypertensive,
        Scenario.NoisySensor, Scenario.Stable, Scenario.Dropout, Scenario.Copd, Scenario.Hypertensive
    ];

    public static Scenario For(int index) => Block[index % Block.Length];

    public static string[] ConditionsFor(Scenario scenario, Random rng) => scenario switch
    {
        Scenario.Copd => rng.NextDouble() < 0.3 ? ["COPD", "HeartFailure"] : ["COPD"],
        Scenario.Hypertensive => rng.NextDouble() < 0.4 ? ["Hypertension", "Diabetes"] : ["Hypertension"],
        Scenario.Deteriorating => rng.NextDouble() < 0.5 ? ["PostSurgical"] : ["Pneumonia"],
        Scenario.Stable => rng.NextDouble() < 0.3 ? ["Diabetes"] : rng.NextDouble() < 0.3 ? ["HeartFailure"] : [],
        _ => rng.NextDouble() < 0.5 ? ["Hypertension"] : []
    };
}
