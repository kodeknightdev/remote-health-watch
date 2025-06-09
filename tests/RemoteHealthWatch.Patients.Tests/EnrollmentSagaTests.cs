using RemoteHealthWatch.Contracts.Events;
using RemoteHealthWatch.Patients.Domain;

namespace RemoteHealthWatch.Patients.Tests;

public class EnrollmentSagaTests
{
    private static readonly DateTimeOffset T0 = new(2025, 4, 16, 9, 0, 0, TimeSpan.Zero);
    private static Patient Enrolled() => new() { Id = Guid.NewGuid(), Status = MonitoringStatus.PendingDevice };

    [Fact]
    public void Happy_path_enrolled_paired_baseline_active()
    {
        var p = Enrolled();
        EnrollmentStateMachine.OnDevicePaired(p, T0)!.To.ShouldBe(MonitoringStatus.BaselineBuilding);
        EnrollmentStateMachine.OnBaselineElapsed(p, T0.AddHours(23), TimeSpan.FromHours(24)).ShouldBeNull();
        EnrollmentStateMachine.OnBaselineElapsed(p, T0.AddHours(24), TimeSpan.FromHours(24))!.To.ShouldBe(MonitoringStatus.MonitoringActive);
        p.MonitoringActiveAt.ShouldBe(T0.AddHours(24));
    }

    [Fact]
    public void Unpairing_the_last_device_compensates_back_to_pending()
    {
        var p = Enrolled();
        EnrollmentStateMachine.OnDevicePaired(p, T0);
        EnrollmentStateMachine.OnBaselineElapsed(p, T0.AddHours(25), TimeSpan.FromHours(24));

        var t = EnrollmentStateMachine.OnDeviceUnpaired(p)!;
        t.From.ShouldBe(MonitoringStatus.MonitoringActive);
        t.To.ShouldBe(MonitoringStatus.PendingDevice);
        p.BaselineStartedAt.ShouldBeNull();
    }

    [Fact]
    public void Second_device_keeps_monitoring_when_one_is_unpaired()
    {
        var p = Enrolled();
        EnrollmentStateMachine.OnDevicePaired(p, T0);
        EnrollmentStateMachine.OnDevicePaired(p, T0.AddMinutes(5)).ShouldBeNull();
        EnrollmentStateMachine.OnDeviceUnpaired(p).ShouldBeNull();
        p.Status.ShouldBe(MonitoringStatus.BaselineBuilding);
    }

    [Fact]
    public void Failed_pairing_records_the_reason_and_stays_pending()
    {
        var p = Enrolled();
        EnrollmentStateMachine.OnPairingFailed(p, "unknown-serial").ShouldBeNull();
        p.Status.ShouldBe(MonitoringStatus.PendingDevice);
        p.LastPairingError.ShouldBe("unknown-serial");
    }

    [Fact]
    public void Discharge_is_final_and_ignores_late_device_events()
    {
        var p = Enrolled();
        EnrollmentStateMachine.OnDevicePaired(p, T0);
        EnrollmentStateMachine.Discharge(p, T0.AddDays(10))!.To.ShouldBe(MonitoringStatus.Discharged);
        EnrollmentStateMachine.OnDeviceUnpaired(p).ShouldBeNull();
        EnrollmentStateMachine.Discharge(p, T0.AddDays(11)).ShouldBeNull();
        p.Status.ShouldBe(MonitoringStatus.Discharged);
    }
}
