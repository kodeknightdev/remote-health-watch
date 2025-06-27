import json
from datetime import UTC, date, datetime, timedelta

from app import kpis, reports
from app.blob import MemoryBlobStore
from app.events import Envelope, Topics
from app.facts import FactHandlers
from app.models import AlertFact, HourlyVital

PATIENT = "8c1d7a52-3b1e-4f6a-9a0e-2a1b3c4d5e6f"
WEEK = date(2025, 5, 19)


def env(data: dict, occurred: str = "2025-05-20T10:00:00+00:00") -> Envelope:
    return Envelope(
        id="e", type="t", schema_version=1, occurred_at=occurred, source="test", correlation_id=None, data=data
    )


def seed(session):
    f = FactHandlers()
    f.patients(
        session,
        env(
            {
                "patientId": PATIENT,
                "enrolledAt": "2025-05-12T08:00:00+00:00",
                "plan": {"vitals": ["HeartRate"], "intervalSeconds": 60},
            }
        ),
        Topics.PATIENT_ENROLLED,
    )
    for day in range(7):
        for hour in range(0, 24):
            at = datetime(2025, 5, 19 + day, hour, 30, tzinfo=UTC)
            readings = [
                {"vital": "HeartRate", "value": 70 + day, "measuredAt": at.isoformat()},
                {"vital": "SpO2", "value": 96 - day * 0.5, "measuredAt": at.isoformat()},
            ]
            for _ in range(60 if hour < 20 else 0):
                f.readings(session, env({"patientId": PATIENT, "readings": readings}), Topics.READINGS_NORMALIZED)
    f.scores(
        session,
        env({"patientId": PATIENT, "score": 6, "band": "Medium", "computedAt": "2025-05-21T12:00:00+00:00"}),
        Topics.SCORE_UPDATED,
    )
    f.alerts(
        session,
        env(
            {
                "alertId": "a1",
                "patientId": PATIENT,
                "type": "ThresholdBreach",
                "severity": "Critical",
                "raisedAt": "2025-05-21T12:00:00+00:00",
            }
        ),
        Topics.ALERT_RAISED,
    )
    f.alerts(
        session,
        env({"alertId": "a1", "patientId": PATIENT, "from": "NotifiedNurse", "to": "EscalatedDoctor"}),
        Topics.ALERT_ESCALATED,
    )
    f.alerts(
        session,
        env(
            {
                "alertId": "a1",
                "patientId": PATIENT,
                "at": "2025-05-21T12:04:00+00:00",
                "secondsToAcknowledge": 240.0,
                "severity": "Critical",
            }
        ),
        Topics.ALERT_ACKNOWLEDGED,
    )
    session.flush()


def test_readings_fold_into_hourly_aggregates(session):
    seed(session)
    row = session.get(HourlyVital, (PATIENT, "HeartRate", datetime(2025, 5, 19, 0)))
    assert row.count == 60
    assert row.total / row.count == 70


def test_acknowledgement_before_raised_still_lands(session):
    f = FactHandlers()
    f.alerts(
        session,
        env({"alertId": "late", "patientId": PATIENT, "at": "2025-05-21T12:04:00+00:00", "secondsToAcknowledge": 30.0}),
        Topics.ALERT_ACKNOWLEDGED,
    )
    f.alerts(
        session,
        env(
            {
                "alertId": "late",
                "patientId": PATIENT,
                "type": "Anomaly",
                "severity": "Medium",
                "raisedAt": "2025-05-21T12:03:30+00:00",
            }
        ),
        Topics.ALERT_RAISED,
    )
    fact = session.get(AlertFact, "late")
    assert fact.severity == "Medium" and fact.seconds_to_ack == 30.0


def test_first_nurse_page_is_not_counted_as_escalation(session):
    f = FactHandlers()
    f.alerts(
        session,
        env(
            {
                "alertId": "x",
                "patientId": PATIENT,
                "type": "ThresholdBreach",
                "severity": "High",
                "raisedAt": "2025-05-21T12:00:00+00:00",
            }
        ),
        Topics.ALERT_RAISED,
    )
    f.alerts(
        session,
        env({"alertId": "x", "patientId": PATIENT, "from": "Raised", "to": "NotifiedNurse"}),
        Topics.ALERT_ESCALATED,
    )
    assert session.get(AlertFact, "x").escalations == 0


def test_kpis_from_facts(session):
    seed(session)
    result = kpis.compute(session, days=14, now=datetime(2025, 5, 26, 0, tzinfo=UTC))
    assert result.enrolled_patients == 1 and result.active_patients == 1
    assert result.average_minutes_to_acknowledge == 4.0
    assert result.escalation_rate == 1.0
    assert 0 < result.wear_time_compliance < 1


def test_weekly_report_renders_pdf_and_emits_claim_check(session):
    seed(session)
    blobs = MemoryBlobStore()
    report, event = reports.generate(session, blobs, "reports", PATIENT, WEEK, now=datetime(2025, 5, 26, 6, tzinfo=UTC))

    content, content_type = blobs.blobs[report.blob_name]
    assert content.startswith(b"%PDF")
    assert content_type == "application/pdf"
    assert report.size_bytes == len(content)

    assert event.topic == Topics.REPORT_GENERATED
    assert event.key == PATIENT
    assert event.data["blobName"] == report.blob_name
    # the event carries a pointer, not the document
    assert len(json.dumps(event.data)) < 500


def test_report_data_summarises_the_week(session):
    seed(session)
    data = reports.build_report_data(session, PATIENT, WEEK)
    hr = next(v for v in data.vitals if v.vital == "HeartRate")
    assert hr.daily_avg == [70, 71, 72, 73, 74, 75, 76]
    assert data.daily_max_news2[2] == 6
    assert data.alerts_by_severity == {"Critical": 1}
    assert data.escalated == 1
    # 20 of 24 hours worn each day
    assert data.daily_wear[0] == round(20 / 24, 3)
    assert data.patient_ref == PATIENT[:8]


def test_week_start_is_monday():
    assert reports.week_start_for(date(2025, 5, 25)) == WEEK
    assert reports.week_start_for(WEEK) == WEEK
    assert reports.week_start_for(WEEK + timedelta(days=7)) == WEEK + timedelta(days=7)
