using System.Text.RegularExpressions;

namespace RemoteHealthWatch.Alerts.Data;

/// <summary>Loads the embedded Quartz job store script and splits it on GO the way sqlcmd would.</summary>
public static partial class QuartzSchema
{
    public static IEnumerable<string> Batches()
    {
        using var stream = typeof(QuartzSchema).Assembly.GetManifestResourceStream("RemoteHealthWatch.Alerts.Data.Sql.quartz-tables.sql")
            ?? throw new InvalidOperationException("quartz-tables.sql is not embedded");
        using var reader = new StreamReader(stream);
        return GoSeparator().Split(reader.ReadToEnd()).Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
