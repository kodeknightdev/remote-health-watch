from datetime import UTC, datetime, timedelta

import numpy as np

from app import trend


def _minutes(hours: float, start_value: float, slope_per_hour: float, noise: float, seed: int):
    rng = np.random.default_rng(seed)
    start = datetime(2025, 5, 5, 6, tzinfo=UTC)
    return [
        (start + timedelta(minutes=m), start_value + slope_per_hour * m / 60 + rng.normal(0, noise))
        for m in range(int(hours * 60))
    ]


def test_rising_rr_with_falling_spo2_is_deterioration():
    result = trend.evaluate(_minutes(6, 16, 1.5, 0.8, 1), _minutes(6, 97, -0.8, 0.4, 2), window_hours=6)
    assert result is not None and result.deteriorating
    assert result.rr_slope_per_hour > 1.0
    assert result.spo2_slope_per_hour < -0.5


def test_stable_patient_is_not_flagged():
    result = trend.evaluate(_minutes(6, 16, 0, 0.8, 3), _minutes(6, 97, 0, 0.4, 4), window_hours=6)
    assert result is not None and not result.deteriorating


def test_rising_rr_alone_is_not_enough():
    result = trend.evaluate(_minutes(6, 16, 1.5, 0.8, 5), _minutes(6, 97, 0, 0.4, 6), window_hours=6)
    assert result is not None and not result.deteriorating


def test_short_history_gives_no_verdict():
    assert trend.evaluate(_minutes(2, 16, 3, 0.5, 7), _minutes(2, 97, -2, 0.3, 8), window_hours=6) is None
