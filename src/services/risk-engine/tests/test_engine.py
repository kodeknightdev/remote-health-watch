from datetime import UTC, datetime, timedelta

from app.engine import RiskEngine
from app.events import Topics
from app.models import RiskScore

PATIENT = "6f1c2c1e-6a7b-4a3a-9d55-1f1a2b3c4d5e"
DEVICE = "0b5b2c4e-1111-4a3a-9d55-1f1a2b3c4d5e"


def batch(at: datetime, hr=72, spo2=97, rr=16, temp=36.8, sbp=125, seq=1, late=False):
    def r(vital, value, value2=None, offset=0):
        return {
            "seq": seq * 10 + offset,
            "vital": vital,
            "value": value,
            "value2": value2,
            "unit": "",
            "measuredAt": at.isoformat(),
            "late": late,
        }

    return {
        "patientId": PATIENT,
        "deviceId": DEVICE,
        "readings": [
            r("HeartRate", hr, offset=1),
            r("SpO2", spo2, offset=2),
            r("RespiratoryRate", rr, offset=3),
            r("Temperature", temp, offset=4),
            r("BloodPressure", sbp, 80, offset=5),
        ],
    }


def test_first_batch_publishes_a_score(session, settings):
    engine = RiskEngine(settings)
    now = datetime(2025, 5, 6, 10, tzinfo=UTC)
    out = engine.process(session, batch(now), now)
    scores = [e for e in out if e.topic == Topics.SCORE_UPDATED]
    assert len(scores) == 1
    assert scores[0].data["score"] == 0
    assert scores[0].data["band"] == "Low"
    assert scores[0].key == PATIENT


def test_unchanged_score_is_not_republished_until_heartbeat(session, settings):
    engine = RiskEngine(settings)
    t0 = datetime(2025, 5, 6, 10, tzinfo=UTC)
    engine.process(session, batch(t0, seq=1), t0)
    t1 = t0 + timedelta(seconds=5)
    assert not [e for e in engine.process(session, batch(t1, seq=2), t1) if e.topic == Topics.SCORE_UPDATED]
    t2 = t0 + timedelta(minutes=settings.score_heartbeat_minutes, seconds=1)
    assert [e for e in engine.process(session, batch(t2, seq=3), t2) if e.topic == Topics.SCORE_UPDATED]


def test_band_change_carries_previous_band(session, settings):
    engine = RiskEngine(settings)
    t0 = datetime(2025, 5, 6, 10, tzinfo=UTC)
    engine.process(session, batch(t0, seq=1), t0)
    t1 = t0 + timedelta(seconds=5)
    out = engine.process(session, batch(t1, hr=124, spo2=92, rr=26, temp=39.2, sbp=98, seq=2), t1)
    score = next(e for e in out if e.topic == Topics.SCORE_UPDATED)
    assert score.data["band"] == "High"
    assert score.data["previousBand"] == "Low"


def test_late_reading_does_not_override_newer_values(session, settings):
    engine = RiskEngine(settings)
    t0 = datetime(2025, 5, 6, 10, tzinfo=UTC)
    engine.process(session, batch(t0, seq=5), t0)
    # an older, sicker reading arrives after reconnect; NEWS2 must still use the newest values
    out = engine.process(session, batch(t0 - timedelta(minutes=2), rr=30, seq=3, late=True), t0 + timedelta(seconds=1))
    assert not [e for e in out if e.topic == Topics.SCORE_UPDATED]


def test_copd_profile_uses_scale_2(session, settings):
    engine = RiskEngine(settings)
    now = datetime(2025, 5, 6, 10, tzinfo=UTC)
    engine.upsert_profile(session, PATIENT, spo2_scale=2, on_oxygen=False, conditions=["COPD"], active=True, now=now)
    out = engine.process(session, batch(now, spo2=89), now)
    score = next(e for e in out if e.topic == Topics.SCORE_UPDATED)
    assert score.data["components"]["SpO2"] == 0


def test_discharged_patient_is_not_scored(session, settings):
    engine = RiskEngine(settings)
    now = datetime(2025, 5, 6, 10, tzinfo=UTC)
    engine.upsert_profile(session, PATIENT, spo2_scale=1, on_oxygen=False, conditions=[], active=True, now=now)
    engine.deactivate(session, PATIENT, now)
    assert engine.process(session, batch(now), now) == []
    assert session.query(RiskScore).count() == 0


def test_zscore_anomaly_after_baseline_with_cooldown(session, settings):
    engine = RiskEngine(settings)
    start = datetime(2025, 5, 6, 0, tzinfo=UTC)
    for i in range(60):
        t = start + timedelta(minutes=i * 5)
        engine.process(session, batch(t, hr=72 + (i % 3), seq=i + 1), t)
    now = start + timedelta(hours=6)
    out = engine.process(session, batch(now, hr=125, seq=1000), now)
    anomalies = [e for e in out if e.topic == Topics.ANOMALY_DETECTED]
    assert [a.data["vital"] for a in anomalies] == ["HeartRate"]
    assert anomalies[0].data["method"] == "zscore"

    again = engine.process(session, batch(now + timedelta(minutes=1), hr=126, seq=1001), now + timedelta(minutes=1))
    assert not [e for e in again if e.topic == Topics.ANOMALY_DETECTED]
