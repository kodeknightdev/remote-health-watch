"""Per-patient baselines and rolling z-score anomaly detection."""

from dataclasses import dataclass

import numpy as np

# floor on the standard deviation so a very steady patient doesn't turn every wobble into a 10-sigma event
STD_FLOOR = {
    "HeartRate": 3.0,
    "SpO2": 0.8,
    "RespiratoryRate": 1.0,
    "Temperature": 0.15,
    "BloodPressure": 4.0,
    "Glucose": 8.0,
}


@dataclass(frozen=True)
class Baseline:
    vital: str
    mean: float
    std: float
    samples: int


def build_baseline(vital: str, values: np.ndarray | list[float], min_samples: int) -> Baseline | None:
    arr = np.asarray(values, dtype=float)
    arr = arr[np.isfinite(arr)]
    if arr.size < min_samples:
        return None
    # trim the extremes so a short spike inside the window doesn't widen the baseline
    lo, hi = np.percentile(arr, [2.5, 97.5])
    trimmed = arr[(arr >= lo) & (arr <= hi)]
    if trimmed.size < min_samples // 2:
        trimmed = arr
    std = max(float(np.std(trimmed, ddof=1)) if trimmed.size > 1 else 0.0, STD_FLOOR.get(vital, 1.0))
    return Baseline(vital=vital, mean=float(np.mean(trimmed)), std=std, samples=int(arr.size))


def zscore(baseline: Baseline, value: float) -> float:
    return (value - baseline.mean) / baseline.std


def is_anomalous(baseline: Baseline | None, value: float, threshold: float) -> tuple[bool, float | None]:
    if baseline is None:
        return False, None
    z = zscore(baseline, value)
    return abs(z) >= threshold, round(z, 2)
