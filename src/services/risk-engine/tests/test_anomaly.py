from datetime import UTC, datetime, timedelta

import numpy as np

from app import anomaly, baseline


def test_baseline_needs_minimum_samples():
    assert baseline.build_baseline("HeartRate", [70, 71, 72], min_samples=30) is None


def test_zscore_flags_spike_against_stable_baseline():
    rng = np.random.default_rng(1)
    b = baseline.build_baseline("HeartRate", rng.normal(72, 3, 500), min_samples=30)
    flagged, z = baseline.is_anomalous(b, 74, threshold=3.5)
    assert not flagged
    flagged, z = baseline.is_anomalous(b, 118, threshold=3.5)
    assert flagged and z > 3.5


def test_std_floor_keeps_steady_patient_from_alerting_on_small_wobble():
    b = baseline.build_baseline("SpO2", [97.0] * 200, min_samples=30)
    assert b.std == baseline.STD_FLOOR["SpO2"]
    assert not baseline.is_anomalous(b, 96.0, threshold=3.5)[0]


def test_baseline_trims_short_spike():
    values = [72.0] * 300 + [160.0] * 5
    b = baseline.build_baseline("HeartRate", values, min_samples=30)
    assert abs(b.mean - 72.0) < 1.0


def _series(start: datetime, minutes: int, deteriorate: bool, seed: int):
    rng = np.random.default_rng(seed)
    rows = []
    for m in range(minutes):
        t = start + timedelta(minutes=m)
        drift = (m / minutes) if deteriorate else 0.0
        rows += [
            ("HeartRate", 72 + 40 * drift + rng.normal(0, 2), t),
            ("SpO2", 97 - 8 * drift + rng.normal(0, 0.4), t),
            ("RespiratoryRate", 15 + 10 * drift + rng.normal(0, 0.7), t),
            ("Temperature", 36.8 + 2.0 * drift + rng.normal(0, 0.05), t),
            ("BloodPressure", 125 - 30 * drift + rng.normal(0, 3), t),
        ]
    return rows


def test_isolation_forest_separates_deteriorating_from_stable():
    start = datetime(2025, 5, 1, tzinfo=UTC)
    train_frame = anomaly.minute_frame(_series(start, 24 * 60, deteriorate=False, seed=3))
    model = anomaly.train(train_frame, start + timedelta(hours=24))
    assert model is not None

    stable_next = anomaly.minute_frame(_series(start + timedelta(hours=24), 30, deteriorate=False, seed=4))
    flagged, _ = anomaly.score_latest(model, stable_next)
    assert not flagged

    # end of a sepsis-like slide: tachycardic, febrile, hypoxic, hypotensive
    sick = anomaly.minute_frame(
        [
            ("HeartRate", 118.0, start),
            ("SpO2", 89.0, start),
            ("RespiratoryRate", 26.0, start),
            ("Temperature", 39.0, start),
            ("BloodPressure", 94.0, start),
        ]
    )
    flagged, score = anomaly.score_latest(model, sick)
    assert flagged
    assert score < model.threshold


def test_isolation_forest_waits_for_a_day_of_history():
    now = datetime(2025, 5, 2, tzinfo=UTC)
    assert not anomaly.has_enough_history(now - timedelta(hours=10), now, 24)
    assert anomaly.has_enough_history(now - timedelta(hours=25), now, 24)
