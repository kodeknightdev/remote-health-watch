"""Turns a normalized readings batch into NEWS2 scores, anomalies and trend flags."""

import json
import logging
from datetime import datetime, timedelta

from sqlalchemy import delete, func, select
from sqlalchemy.orm import Session

from app import anomaly, baseline, news2, trend
from app.config import Settings
from app.db import aware, naive
from app.events import EventTypes, Outgoing, Topics
from app.models import AnomalyRow, BaselineRow, Observation, PatientProfile, RiskScore, TrendFlag

log = logging.getLogger(__name__)

NEWS2_VITALS = ("RespiratoryRate", "SpO2", "BloodPressure", "HeartRate", "Temperature")
BASELINE_REFRESH = timedelta(minutes=10)
# readings in the most recent half hour stay out of the baseline, otherwise a developing problem becomes "normal"
BASELINE_GAP = timedelta(minutes=30)
TREND_CHECK_EVERY = timedelta(minutes=10)


class RiskEngine:
    def __init__(self, settings: Settings):
        self.settings = settings
        self._models: dict[str, anomaly.PatientModel] = {}
        self._trend_checked: dict[str, datetime] = {}

    def upsert_profile(self, s: Session, patient_id: str, *, spo2_scale: int, on_oxygen: bool,
                       conditions: list[str], active: bool, now: datetime) -> None:
        profile = s.get(PatientProfile, patient_id)
        if profile is None:
            profile = PatientProfile(patient_id=patient_id)
            s.add(profile)
        profile.spo2_scale = spo2_scale
        profile.on_oxygen = on_oxygen
        profile.conditions = ",".join(conditions)
        profile.active = active
        profile.updated_at = naive(now)

    def deactivate(self, s: Session, patient_id: str, now: datetime) -> None:
        profile = s.get(PatientProfile, patient_id)
        if profile is not None:
            profile.active = False
            profile.updated_at = naive(now)
        self._models.pop(patient_id, None)

    def process(self, s: Session, batch: dict, now: datetime) -> list[Outgoing]:
        patient_id = batch["patientId"]
        profile = s.get(PatientProfile, patient_id)
        if profile is not None and not profile.active:
            return []
        readings = batch.get("readings") or []
        if not readings:
            return []

        for r in readings:
            s.add(Observation(
                patient_id=patient_id,
                vital=r["vital"],
                value=float(r["value"]),
                value2=float(r["value2"]) if r.get("value2") is not None else None,
                measured_at=naive(_parse(r["measuredAt"])),
            ))
        s.flush()

        out: list[Outgoing] = []
        score_event = self._score(s, patient_id, profile, now)
        if score_event:
            out.append(score_event)
        out.extend(self._zscore_anomalies(s, patient_id, [r for r in readings if not r.get("late")], now))
        iforest_event = self._iforest(s, patient_id, now)
        if iforest_event:
            out.append(iforest_event)
        trend_event = self._trend(s, patient_id, now)
        if trend_event:
            out.append(trend_event)
        return out

    def _latest_observation(self, s: Session, patient_id: str, now: datetime) -> dict[str, float] | None:
        since = naive(now - timedelta(minutes=self.settings.news2_window_minutes))
        newest = (
            select(Observation.vital, func.max(Observation.measured_at).label("at"))
            .where(Observation.patient_id == patient_id, Observation.measured_at >= since,
                   Observation.vital.in_(NEWS2_VITALS))
            .group_by(Observation.vital)
            .subquery()
        )
        rows = s.execute(
            select(Observation.vital, Observation.value)
            .join(newest, (Observation.vital == newest.c.vital) & (Observation.measured_at == newest.c.at))
            .where(Observation.patient_id == patient_id)
        ).all()
        return {v: val for v, val in rows} or None

    def _score(self, s: Session, patient_id: str, profile: PatientProfile | None, now: datetime) -> Outgoing | None:
        latest = self._latest_observation(s, patient_id, now)
        if not latest:
            return None
        obs = news2.Observation(
            respiratory_rate=latest.get("RespiratoryRate"),
            spo2=latest.get("SpO2"),
            on_oxygen=bool(profile and profile.on_oxygen),
            systolic=latest.get("BloodPressure"),
            pulse=latest.get("HeartRate"),
            temperature=latest.get("Temperature"),
        )
        result = news2.compute(obs, spo2_scale=profile.spo2_scale if profile else 1)

        previous = s.execute(
            select(RiskScore).where(RiskScore.patient_id == patient_id).order_by(RiskScore.computed_at.desc()).limit(1)
        ).scalar_one_or_none()
        heartbeat = timedelta(minutes=self.settings.score_heartbeat_minutes)
        unchanged = (
            previous is not None
            and previous.score == result.score
            and previous.band == result.band.value
            and now - aware(previous.computed_at) < heartbeat
        )
        if unchanged:
            return None

        s.add(RiskScore(
            patient_id=patient_id,
            score=result.score,
            band=result.band.value,
            components=json.dumps(result.components),
            complete=result.complete,
            computed_at=naive(now),
        ))
        return Outgoing(Topics.SCORE_UPDATED, patient_id, EventTypes.SCORE_UPDATED, {
            "patientId": patient_id,
            "score": result.score,
            "band": result.band.value,
            "previousBand": previous.band if previous else None,
            "components": result.components,
            "computedAt": now.isoformat(),
        })

    def _baseline(self, s: Session, patient_id: str, vital: str, now: datetime) -> baseline.Baseline | None:
        row = s.get(BaselineRow, (patient_id, vital))
        if row is not None and now - aware(row.computed_at) < BASELINE_REFRESH:
            return baseline.Baseline(vital, row.mean, row.std, row.samples)

        start = naive(now - timedelta(hours=self.settings.baseline_hours))
        end = naive(now - BASELINE_GAP)
        values = s.execute(
            select(Observation.value).where(
                Observation.patient_id == patient_id, Observation.vital == vital,
                Observation.measured_at >= start, Observation.measured_at < end)
        ).scalars().all()
        b = baseline.build_baseline(vital, values, self.settings.baseline_min_samples)
        if b is None:
            return None
        if row is None:
            row = BaselineRow(patient_id=patient_id, vital=vital)
            s.add(row)
        row.mean, row.std, row.samples, row.computed_at = b.mean, b.std, b.samples, naive(now)
        return b

    def _cooling_down(self, s: Session, patient_id: str, vital: str, method: str, now: datetime) -> bool:
        since = naive(now - timedelta(minutes=self.settings.anomaly_cooldown_minutes))
        return s.execute(
            select(AnomalyRow.id).where(
                AnomalyRow.patient_id == patient_id, AnomalyRow.vital == vital,
                AnomalyRow.method == method, AnomalyRow.detected_at >= since).limit(1)
        ).first() is not None

    def _zscore_anomalies(self, s: Session, patient_id: str, readings: list[dict], now: datetime) -> list[Outgoing]:
        out = []
        newest_by_vital: dict[str, dict] = {}
        for r in readings:
            if r["vital"] not in newest_by_vital or _parse(r["measuredAt"]) > _parse(newest_by_vital[r["vital"]]["measuredAt"]):
                newest_by_vital[r["vital"]] = r
        for vital, r in newest_by_vital.items():
            b = self._baseline(s, patient_id, vital, now)
            flagged, z = baseline.is_anomalous(b, float(r["value"]), self.settings.zscore_threshold)
            if not flagged or self._cooling_down(s, patient_id, vital, "zscore", now):
                continue
            measured = _parse(r["measuredAt"])
            s.add(AnomalyRow(patient_id=patient_id, vital=vital, value=float(r["value"]), zscore=z, method="zscore",
                             measured_at=naive(measured), detected_at=naive(now)))
            out.append(Outgoing(Topics.ANOMALY_DETECTED, patient_id, EventTypes.ANOMALY_DETECTED, {
                "patientId": patient_id, "vital": vital, "value": float(r["value"]), "zScore": z,
                "method": "zscore", "measuredAt": measured.isoformat(),
            }))
        return out

    def _history_rows(self, s: Session, patient_id: str, since: datetime) -> list[tuple[str, float, datetime]]:
        return [
            (v, val, aware(at)) for v, val, at in s.execute(
                select(Observation.vital, Observation.value, Observation.measured_at)
                .where(Observation.patient_id == patient_id, Observation.measured_at >= naive(since))
                .order_by(Observation.measured_at)
            ).all()
        ]

    def _iforest(self, s: Session, patient_id: str, now: datetime) -> Outgoing | None:
        first_seen = s.execute(
            select(func.min(Observation.measured_at)).where(Observation.patient_id == patient_id)
        ).scalar_one_or_none()
        if not anomaly.has_enough_history(aware(first_seen) if first_seen else None, now, self.settings.iforest_min_hours):
            return None

        model = self._models.get(patient_id)
        if model is None or now - model.trained_at > timedelta(minutes=self.settings.iforest_retrain_minutes):
            history = self._history_rows(s, patient_id, now - timedelta(hours=self.settings.baseline_hours))
            frame = anomaly.minute_frame([r for r in history if r[2] < now - BASELINE_GAP])
            model = anomaly.train(frame, now)
            if model is None:
                return None
            self._models[patient_id] = model

        recent = anomaly.minute_frame(self._history_rows(s, patient_id, now - timedelta(minutes=30)))
        scored = anomaly.score_latest(model, recent)
        if not scored or not scored[0]:
            return None

        # name the feature furthest from its baseline so the alert says something useful
        latest = recent.tail(1).iloc[0]
        worst, worst_z = None, 0.0
        for vital in anomaly.FEATURES:
            b = self._baseline(s, patient_id, vital, now)
            if b is None:
                continue
            z = baseline.zscore(b, float(latest[vital]))
            if abs(z) > abs(worst_z):
                worst, worst_z = vital, z
        vital = worst or "HeartRate"
        if self._cooling_down(s, patient_id, vital, "isolation-forest", now):
            return None
        s.add(AnomalyRow(patient_id=patient_id, vital=vital, value=float(latest[vital]), zscore=round(worst_z, 2),
                         method="isolation-forest", measured_at=naive(now), detected_at=naive(now)))
        return Outgoing(Topics.ANOMALY_DETECTED, patient_id, EventTypes.ANOMALY_DETECTED, {
            "patientId": patient_id, "vital": vital, "value": float(latest[vital]), "zScore": round(worst_z, 2),
            "method": "isolation-forest", "measuredAt": now.isoformat(),
        })

    def _trend(self, s: Session, patient_id: str, now: datetime) -> Outgoing | None:
        last = self._trend_checked.get(patient_id)
        if last is not None and now - last < TREND_CHECK_EVERY:
            return None
        self._trend_checked[patient_id] = now

        window = timedelta(hours=self.settings.trend_window_hours)
        rows = self._history_rows(s, patient_id, now - window)
        rr = _minute_means([r for r in rows if r[0] == "RespiratoryRate"])
        spo2 = _minute_means([r for r in rows if r[0] == "SpO2"])
        result = trend.evaluate(rr, spo2, self.settings.trend_window_hours)
        if result is None or not result.deteriorating:
            return None

        since = naive(now - timedelta(minutes=self.settings.trend_cooldown_minutes))
        recent_flag = s.execute(
            select(TrendFlag.id).where(TrendFlag.patient_id == patient_id, TrendFlag.detected_at >= since).limit(1)
        ).first()
        if recent_flag:
            return None
        s.add(TrendFlag(patient_id=patient_id, rr_slope=result.rr_slope_per_hour, spo2_slope=result.spo2_slope_per_hour,
                        detected_at=naive(now)))
        return Outgoing(Topics.DETERIORATION_TREND, patient_id, EventTypes.DETERIORATION_TREND, {
            "patientId": patient_id,
            "respiratoryRateSlopePerHour": result.rr_slope_per_hour,
            "spO2SlopePerHour": result.spo2_slope_per_hour,
            "windowHours": int(self.settings.trend_window_hours),
            "detectedAt": now.isoformat(),
        })

    def purge(self, s: Session, now: datetime) -> int:
        cutoff = naive(now - timedelta(hours=self.settings.observation_retention_hours))
        return s.execute(delete(Observation).where(Observation.measured_at < cutoff)).rowcount or 0


def _minute_means(rows: list[tuple[str, float, datetime]]) -> list[tuple[datetime, float]]:
    buckets: dict[datetime, list[float]] = {}
    for _, value, at in rows:
        buckets.setdefault(at.replace(second=0, microsecond=0), []).append(value)
    return [(t, sum(v) / len(v)) for t, v in sorted(buckets.items())]


def _parse(value: str) -> datetime:
    return aware(datetime.fromisoformat(value.replace("Z", "+00:00")))
