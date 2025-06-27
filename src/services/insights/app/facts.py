"""Folds the event streams into the small fact tables reports and KPIs are built from."""

from datetime import datetime

from sqlalchemy.orm import Session

from app.db import aware, naive
from app.events import Envelope, Outgoing, Topics
from app.models import AlertFact, DailyScore, HourlyVital, PatientFact


def _ts(value: str) -> datetime:
    return aware(datetime.fromisoformat(value.replace("Z", "+00:00")))


class FactHandlers:
    def patients(self, s: Session, env: Envelope, topic: str) -> list[Outgoing]:
        d = env.data
        fact = s.get(PatientFact, d["patientId"])
        if topic == Topics.PATIENT_ENROLLED:
            if fact is None:
                fact = PatientFact(patient_id=d["patientId"])
                s.add(fact)
            fact.enrolled_at = naive(_ts(d["enrolledAt"]))
            fact.interval_seconds = int((d.get("plan") or {}).get("intervalSeconds", 60))
            fact.discharged_at = None
        elif topic == Topics.PATIENT_DISCHARGED and fact is not None:
            fact.discharged_at = naive(_ts(d["dischargedAt"]))
        return []

    def readings(self, s: Session, env: Envelope, topic: str) -> list[Outgoing]:
        d = env.data
        patient_id = d["patientId"]
        buckets: dict[tuple[str, datetime], list[float]] = {}
        for r in d.get("readings") or []:
            hour = naive(_ts(r["measuredAt"])).replace(minute=0, second=0, microsecond=0)
            buckets.setdefault((r["vital"], hour), []).append(float(r["value"]))
        for (vital, hour), values in buckets.items():
            row = s.get(HourlyVital, (patient_id, vital, hour))
            if row is None:
                row = HourlyVital(
                    patient_id=patient_id,
                    vital=vital,
                    hour=hour,
                    count=0,
                    total=0.0,
                    min_value=min(values),
                    max_value=max(values),
                )
                s.add(row)
            row.count += len(values)
            row.total += sum(values)
            row.min_value = min(row.min_value, *values)
            row.max_value = max(row.max_value, *values)
        return []

    def scores(self, s: Session, env: Envelope, topic: str) -> list[Outgoing]:
        d = env.data
        day = _ts(d["computedAt"]).date()
        row = s.get(DailyScore, (d["patientId"], day))
        if row is None:
            s.add(DailyScore(patient_id=d["patientId"], day=day, max_score=int(d["score"]), last_band=d["band"]))
        else:
            row.max_score = max(row.max_score, int(d["score"]))
            row.last_band = d["band"]
        return []

    def alerts(self, s: Session, env: Envelope, topic: str) -> list[Outgoing]:
        d = env.data
        fact = s.get(AlertFact, d["alertId"])
        if fact is None:
            # lifecycle events can overtake alert-raised across partitions of different topics
            fact = AlertFact(
                alert_id=d["alertId"], patient_id=d["patientId"], raised_at=naive(_ts(env.occurred_at)), escalations=0
            )
            s.add(fact)
        if topic == Topics.ALERT_RAISED:
            fact.type = d.get("type", "")
            fact.severity = d.get("severity", "")
            fact.raised_at = naive(_ts(d["raisedAt"]))
        elif topic == Topics.ALERT_ACKNOWLEDGED:
            fact.acknowledged_at = naive(_ts(d["at"]))
            fact.seconds_to_ack = float(d["secondsToAcknowledge"])
            fact.severity = fact.severity or d.get("severity", "")
        elif topic == Topics.ALERT_ESCALATED:
            # the first move (Raised -> NotifiedNurse) is not an escalation
            if d.get("from") not in (None, "Raised"):
                fact.escalations = (fact.escalations or 0) + 1
        elif topic == Topics.ALERT_RESOLVED:
            fact.resolved_at = naive(_ts(d["at"]))
        return []
