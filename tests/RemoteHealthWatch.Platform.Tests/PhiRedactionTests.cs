using RemoteHealthWatch.ServiceDefaults.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RemoteHealthWatch.Platform.Tests;

public class PhiRedactionTests
{
    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (ILogger Logger, Capture Sink) NewLogger()
    {
        var sink = new Capture();
        var logger = new LoggerConfiguration().Enrich.With<PhiRedactionEnricher>().WriteTo.Sink(sink).CreateLogger();
        return (logger, sink);
    }

    [Fact]
    public void Named_phi_properties_are_masked()
    {
        var (log, sink) = NewLogger();
        log.Information("Enrolled {FirstName} {LastName} born {DateOfBirth}", "Agnes", "Holloway", "1948-03-07");
        var props = sink.Events.Single().Properties;
        props["FirstName"].ToString().ShouldContain(PhiRedactionEnricher.Mask);
        props["LastName"].ToString().ShouldContain(PhiRedactionEnricher.Mask);
        props["DateOfBirth"].ToString().ShouldContain(PhiRedactionEnricher.Mask);
    }

    [Fact]
    public void Surrogate_ids_pass_through()
    {
        var (log, sink) = NewLogger();
        var id = Guid.NewGuid();
        log.Information("Patient {PatientId} moved to {Status}", id, "MonitoringActive");
        sink.Events.Single().Properties["PatientId"].ToString().ShouldContain(id.ToString());
    }

    [Fact]
    public void Destructured_objects_are_masked_field_by_field()
    {
        var (log, sink) = NewLogger();
        log.Information("Request {@Request}", new { FirstName = "Agnes", Mrn = "MRN-100042", Conditions = new[] { "COPD" } });
        var rendered = sink.Events.Single().Properties["Request"].ToString();
        rendered.ShouldNotContain("Agnes");
        rendered.ShouldNotContain("MRN-100042");
        rendered.ShouldContain("COPD");
    }

    [Fact]
    public void Dob_and_mrn_shaped_strings_are_masked_under_any_name()
    {
        var (log, sink) = NewLogger();
        log.Information("Lookup {Term} then {Other}", "1951-11-30", "MRN-123456");
        var props = sink.Events.Single().Properties;
        props["Term"].ToString().ShouldContain(PhiRedactionEnricher.Mask);
        props["Other"].ToString().ShouldContain(PhiRedactionEnricher.Mask);
    }
}
