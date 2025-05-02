"""Multivariate anomaly detection with IsolationForest once a patient has a full day of data."""

from dataclasses import dataclass
from datetime import datetime, timedelta

import numpy as np
import pandas as pd
from sklearn.ensemble import IsolationForest

FEATURES = ["HeartRate", "SpO2", "RespiratoryRate", "Temperature", "BloodPressure"]


def minute_frame(rows: list[tuple[str, float, datetime]]) -> pd.DataFrame:
    """Pivot raw observations into one row per minute with a column per vital, forward-filled for sparse sensors."""
    if not rows:
        return pd.DataFrame(columns=FEATURES)
    df = pd.DataFrame(rows, columns=["vital", "value", "at"])
    df = df[df["vital"].isin(FEATURES)]
    if df.empty:
        return pd.DataFrame(columns=FEATURES)
    df["at"] = pd.to_datetime(df["at"], utc=True).dt.floor("min")
    wide = df.pivot_table(index="at", columns="vital", values="value", aggfunc="mean").sort_index()
    wide = wide.reindex(columns=FEATURES).ffill(limit=30)
    return wide.dropna(how="any")


@dataclass
class PatientModel:
    model: IsolationForest
    trained_at: datetime
    threshold: float


def has_enough_history(first_seen: datetime | None, now: datetime, min_hours: float) -> bool:
    return first_seen is not None and now - first_seen >= timedelta(hours=min_hours)


def train(frame: pd.DataFrame, now: datetime, contamination: float = 0.01) -> PatientModel | None:
    if len(frame) < 120:
        return None
    model = IsolationForest(n_estimators=150, contamination=contamination, random_state=7)
    model.fit(frame.to_numpy())
    # decision_function < 0 marks the contamination tail; keep a margin so borderline minutes don't alert
    scores = model.decision_function(frame.to_numpy())
    threshold = float(min(0.0, np.percentile(scores, 0.5)))
    return PatientModel(model=model, trained_at=now, threshold=threshold)


def score_latest(pm: PatientModel, frame: pd.DataFrame) -> tuple[bool, float] | None:
    if frame.empty:
        return None
    latest = frame.tail(1).to_numpy()
    score = float(pm.model.decision_function(latest)[0])
    return score < pm.threshold, round(score, 4)
