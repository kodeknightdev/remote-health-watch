"""Programme KPIs. The arithmetic lives in plain functions so it can be tested without a database."""

from collections import Counter
from dataclasses import dataclass
from datetime import date, datetime, timedelta

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.db import aware, naive
from app.models import AlertFact, HourlyVital, PatientFact

# the multi-parameter patch always sends heart rate, so it stands in for "device was worn"
WEAR_VITAL = "HeartRate"


@dataclass(frozen=True)
class KpiResult:
    enrolled_patients: int
    active_patients: int
    alerts_per_day: list[tuple[date, int]]
    average_minutes_to_acknowledge: float | None
    escalation_rate: float
    wear_time_compliance: float | None


def alerts_per_day(raised: list[datetime], start: date, end: date) -> list[tuple[date, int]]:
    counts = Counter(r.date() for r in raised)
    days = (end - start).days + 1
    return [(start + timedelta(days=i), counts.get(start + timedelta(days=i), 0)) for i in range(days)]


def average_minutes_to_ack(seconds: list[float]) -> float | None:
    return round(sum(seconds) / len(seconds) / 60.0, 2) if seconds else None


def escalation_rate(escalations: list[int]) -> float:
    return round(sum(1 for e in escalations if e > 0) / len(escalations), 3) if escalations else 0.0


def wear_fraction(readings_in_hour: int, interval_seconds: int) -> float:
    expected = max(1.0, 3600.0 / max(interval_seconds, 1))
    return min(1.0, readings_in_hour / expected)


def wear_compliance(hours: list[tuple[int, int]], expected_hours: int) -> float | None:
    """Share of the expected monitoring hours covered, crediting partial hours by reading count."""
    if expected_hours <= 0:
        return None
    worn = sum(wear_fraction(count, interval) for count, interval in hours)
    return round(min(1.0, worn / expected_hours), 3)


def compute(s: Session, days: int, now: datetime) -> KpiResult:
    since = naive(now - timedelta(days=days))
    patients = s.execute(select(PatientFact)).scalars().all()
    active = [p for p in patients if p.discharged_at is None]

    alerts = s.execute(select(AlertFact).where(AlertFact.raised_at >= since)).scalars().all()
    paged = [a for a in alerts if a.severity and a.severity != "Info"]
    per_day = alerts_per_day([aware(a.raised_at) for a in alerts], (now - timedelta(days=days - 1)).date(), now.date())

    intervals = {p.patient_id: p.interval_seconds for p in patients}
    rows = s.execute(
        select(HourlyVital.patient_id, HourlyVital.count).where(
            HourlyVital.vital == WEAR_VITAL, HourlyVital.hour >= since
        )
    ).all()
    expected_hours = 0
    for p in active:
        start = max(aware(p.enrolled_at), now - timedelta(days=days))
        expected_hours += max(0, int((now - start).total_seconds() // 3600))
    compliance = wear_compliance([(c, intervals.get(pid, 60)) for pid, c in rows if pid in intervals], expected_hours)

    return KpiResult(
        enrolled_patients=len(patients),
        active_patients=len(active),
        alerts_per_day=per_day,
        average_minutes_to_acknowledge=average_minutes_to_ack(
            [a.seconds_to_ack for a in paged if a.seconds_to_ack is not None]
        ),
        escalation_rate=escalation_rate([a.escalations for a in paged]),
        wear_time_compliance=compliance,
    )
