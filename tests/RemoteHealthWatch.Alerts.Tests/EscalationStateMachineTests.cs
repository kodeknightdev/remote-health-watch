using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Escalation;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Events;

namespace RemoteHealthWatch.Alerts.Tests;

public class EscalationStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2025, 6, 9, 10, 0, 0, TimeSpan.Zero);

    private static Alert NewAlert(AlertSeverity severity = AlertSeverity.Critical) => new()
    {
        Id = Guid.NewGuid(), PatientId = Guid.NewGuid(), Severity = severity, Status = AlertStatus.Raised, RaisedAt = T0, Message = "SpO2 low"
    };

    [Fact]
    public void Critical_alert_walks_nurse_doctor_coordinator_on_2_5_10_minutes()
    {
        var alert = NewAlert();
        var start = EscalationStateMachine.Start(alert, T0);
        alert.Status.ShouldBe(AlertStatus.NotifiedNurse);
        start.Commands.OfType<ScheduleTimeout>().Single().DueAt.ShouldBe(T0.AddMinutes(2));
        start.Commands.OfType<NotifyRole>().Single().Role.ShouldBe(Roles.Nurse);

        var toDoctor = EscalationStateMachine.OnTimeout(alert, EscalationStateMachine.Nurse, T0.AddMinutes(2));
        alert.Status.ShouldBe(AlertStatus.EscalatedDoctor);
        toDoctor.Commands.OfType<ScheduleTimeout>().Single().DueAt.ShouldBe(T0.AddMinutes(7));

        var toCoordinator = EscalationStateMachine.OnTimeout(alert, EscalationStateMachine.Doctor, T0.AddMinutes(7));
        alert.Status.ShouldBe(AlertStatus.EscalatedCoordinator);
        toCoordinator.Commands.OfType<ScheduleTimeout>().Single().DueAt.ShouldBe(T0.AddMinutes(17));

        var final = EscalationStateMachine.OnTimeout(alert, EscalationStateMachine.Coordinator, T0.AddMinutes(17));
        alert.EscalationLevel.ShouldBe(EscalationStateMachine.Unanswered);
        alert.NextEscalationAt.ShouldBeNull();
        final.Commands.OfType<NotifyRole>().Count().ShouldBe(3);
        final.Commands.OfType<ScheduleTimeout>().ShouldBeEmpty();

        alert.Timeline.Select(t => t.Kind).ShouldBe([
            TimelineKind.Raised, TimelineKind.NotifiedNurse, TimelineKind.EscalatedDoctor, TimelineKind.EscalatedCoordinator, TimelineKind.Unanswered
        ]);
    }

    [Fact]
    public void Acknowledging_stops_the_clock()
    {
        var alert = NewAlert();
        EscalationStateMachine.Start(alert, T0);
        var ack = EscalationStateMachine.Acknowledge(alert, Guid.NewGuid(), T0.AddSeconds(90));
        alert.Status.ShouldBe(AlertStatus.Acknowledged);
        ack.Commands.ShouldContain(c => c is CancelTimeouts);
        ack.Commands.OfType<AnnounceAcknowledged>().Single().SecondsToAcknowledge.ShouldBe(90);

        EscalationStateMachine.OnTimeout(alert, EscalationStateMachine.Nurse, T0.AddMinutes(2)).ShouldBe(EscalationDecision.NoOp);
        alert.Status.ShouldBe(AlertStatus.Acknowledged);
    }

    [Fact]
    public void Replayed_timeout_after_a_restart_does_not_escalate_twice()
    {
        // the process escalated to the doctor, then crashed before Quartz recorded the trigger as done
        var alert = NewAlert();
        EscalationStateMachine.Start(alert, T0);
        EscalationStateMachine.OnTimeout(alert, EscalationStateMachine.Nurse, T0.AddMinutes(2));

        // on restart Quartz fires the misfired nurse-level trigger again
        var replay = EscalationStateMachine.OnTimeout(alert, EscalationStateMachine.Nurse, T0.AddMinutes(3));
        replay.ShouldBe(EscalationDecision.NoOp);
        alert.Status.ShouldBe(AlertStatus.EscalatedDoctor);
        alert.Timeline.Count(t => t.Kind == TimelineKind.EscalatedDoctor).ShouldBe(1);
    }

    [Fact]
    public void Info_alerts_go_to_the_coordinator_and_never_escalate()
    {
        var alert = NewAlert(AlertSeverity.Info);
        var start = EscalationStateMachine.Start(alert, T0);
        alert.AssignedRole.ShouldBe(Roles.CareCoordinator);
        start.Commands.OfType<ScheduleTimeout>().ShouldBeEmpty();
        EscalationStateMachine.OnTimeout(alert, alert.EscalationLevel, T0.AddHours(1)).ShouldBe(EscalationDecision.NoOp);
    }

    [Fact]
    public void Upgrade_tightens_the_window_at_the_current_level()
    {
        var alert = NewAlert(AlertSeverity.Medium);
        EscalationStateMachine.Start(alert, T0);
        alert.NextEscalationAt.ShouldBe(T0.AddMinutes(15));

        var upgrade = EscalationStateMachine.Upgrade(alert, AlertSeverity.Critical, "SpO2 critical", T0.AddMinutes(1));
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.NextEscalationAt.ShouldBe(T0.AddMinutes(3));
        upgrade.Commands.OfType<ScheduleTimeout>().Single().Level.ShouldBe(EscalationStateMachine.Nurse);
    }

    [Fact]
    public void Resolving_twice_is_a_no_op()
    {
        var alert = NewAlert();
        EscalationStateMachine.Start(alert, T0);
        EscalationStateMachine.Resolve(alert, null, "news2-back-to-low", T0.AddMinutes(1)).Changed.ShouldBeTrue();
        EscalationStateMachine.Resolve(alert, null, "again", T0.AddMinutes(2)).ShouldBe(EscalationDecision.NoOp);
    }
}
