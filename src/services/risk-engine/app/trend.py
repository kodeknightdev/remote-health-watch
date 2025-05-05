"""Slow deterioration: rising respiratory rate together with falling SpO2 over several hours."""

from dataclasses import dataclass
from datetime import datetime

import numpy as np


@dataclass(frozen=True)
class TrendResult:
    rr_slope_per_hour: float
    spo2_slope_per_hour: float
    deteriorating: bool


RR_RISING = 0.8  # breaths/min per hour
SPO2_FALLING = -0.4  # percentage points per hour


def slope_per_hour(points: list[tuple[datetime, float]]) -> float | None:
    if len(points) < 2:
        return None
    t0 = points[0][0]
    x = np.array([(t - t0).total_seconds() / 3600.0 for t, _ in points])
    y = np.array([v for _, v in points])
    if np.ptp(x) == 0:
        return None
    slope, _ = np.polyfit(x, y, 1)
    return float(slope)


def evaluate(
    rr: list[tuple[datetime, float]],
    spo2: list[tuple[datetime, float]],
    window_hours: float,
    min_points: int = 30,
) -> TrendResult | None:
    """Needs both series to cover at least two thirds of the window, otherwise there's nothing to say."""
    for series in (rr, spo2):
        if len(series) < min_points:
            return None
        span = (series[-1][0] - series[0][0]).total_seconds() / 3600.0
        if span < window_hours * 2 / 3:
            return None
    rr_slope = slope_per_hour(rr)
    spo2_slope = slope_per_hour(spo2)
    if rr_slope is None or spo2_slope is None:
        return None
    return TrendResult(
        rr_slope_per_hour=round(rr_slope, 3),
        spo2_slope_per_hour=round(spo2_slope, 3),
        deteriorating=rr_slope >= RR_RISING and spo2_slope <= SPO2_FALLING,
    )
