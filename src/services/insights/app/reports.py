import statistics
import uuid
from collections import Counter
from datetime import UTC, date, datetime, timedelta

from sqlalchemy import select
from sqlalchemy.orm import Session

from app import pdf
from app.blob import BlobStore
from app.db import naive
from app.events import Outgoing, Topics
from app.kpis import WEAR_VITAL, wear_fraction
from app.models import AlertFact, DailyScore, HourlyVital, PatientFact, Report

REPORT_VITALS = ["HeartRate", "SpO2", "RespiratoryRate", "Temperature", "BloodPressure", "Glucose"]


def week_start_for(d: date) -> date:
    return d - timedelta(days=d.weekday())


def build_report_data(s: Session, patient_id: str, week_start: date) -> pdf.ReportData:
    start = datetime.combine(week_start, datetime.min.time())
    end = start + timedelta(days=7)
    hourly = (
        s.execute(
            select(HourlyVital).where(
                HourlyVital.patient_id == patient_id, HourlyVital.hour >= start, HourlyVital.hour < end
            )
        )
        .scalars()
        .all()
    )

    vitals = []
    for vital in REPORT_VITALS:
        rows = [h for h in hourly if h.vital == vital]
        if not rows:
            continue
        daily = []
        for i in range(7):
            day_rows = [h for h in rows if h.hour.date() == week_start + timedelta(days=i)]
            n = sum(h.count for h in day_rows)
            daily.append(round(sum(h.total for h in day_rows) / n, 1) if n else None)
        n = sum(h.count for h in rows)
        vitals.append(
            pdf.VitalWeek(
                vital,
                daily,
                min(h.min_value for h in rows),
                round(sum(h.total for h in rows) / n, 1),
                max(h.max_value for h in rows),
                n,
            )
        )

    patient = s.get(PatientFact, patient_id)
    interval = patient.interval_seconds if patient else 60
    daily_wear: list[float | None] = []
    for i in range(7):
        day = week_start + timedelta(days=i)
        hours = [h for h in hourly if h.vital == WEAR_VITAL and h.hour.date() == day]
        daily_wear.append(round(sum(wear_fraction(h.count, interval) for h in hours) / 24, 3) if hours else None)

    scores = {
        d.day: d.max_score
        for d in s.execute(
            select(DailyScore).where(
                DailyScore.patient_id == patient_id,
                DailyScore.day >= week_start,
                DailyScore.day < week_start + timedelta(days=7),
            )
        ).scalars()
    }
    alerts = (
        s.execute(
            select(AlertFact).where(
                AlertFact.patient_id == patient_id, AlertFact.raised_at >= start, AlertFact.raised_at < end
            )
        )
        .scalars()
        .all()
    )
    acked = [a.seconds_to_ack / 60 for a in alerts if a.seconds_to_ack is not None]
    by_severity = Counter(a.severity or "Unknown" for a in alerts)
    order = ["Critical", "High", "Medium", "Low", "Info", "Unknown"]

    return pdf.ReportData(
        patient_ref=patient_id[:8],
        week_start=week_start,
        vitals=vitals,
        daily_max_news2=[scores.get(week_start + timedelta(days=i)) for i in range(7)],
        alerts_by_severity={k: by_severity[k] for k in order if by_severity.get(k)},
        alerts_acknowledged=len(acked),
        median_minutes_to_ack=round(statistics.median(acked), 1) if acked else None,
        escalated=sum(1 for a in alerts if a.escalations),
        daily_wear=daily_wear,
    )


def generate(
    s: Session, blobs: BlobStore, container: str, patient_id: str, week_start: date, now: datetime | None = None
) -> tuple[Report, Outgoing]:
    now = now or datetime.now(UTC)
    data = build_report_data(s, patient_id, week_start)
    content = pdf.render(data)
    report_id = str(uuid.uuid4())
    blob_name = f"{patient_id}/{week_start.isoformat()}/{report_id}.pdf"
    blobs.put(blob_name, content, "application/pdf")

    report = Report(
        report_id=report_id,
        patient_id=patient_id,
        week_start=week_start,
        container=container,
        blob_name=blob_name,
        size_bytes=len(content),
        generated_at=naive(now),
    )
    s.add(report)
    # claim check: consumers get a pointer to the blob, never the PDF itself
    event = Outgoing(
        Topics.REPORT_GENERATED,
        patient_id,
        "insights.report-generated",
        {
            "reportId": report_id,
            "patientId": patient_id,
            "weekStart": week_start.isoformat(),
            "container": container,
            "blobName": blob_name,
            "generatedAt": now.isoformat(),
        },
    )
    return report, event


def patients_for_weekly_run(s: Session, week_start: date) -> list[str]:
    start = datetime.combine(week_start, datetime.min.time())
    rows = s.execute(select(PatientFact)).scalars().all()
    return [p.patient_id for p in rows if p.discharged_at is None or p.discharged_at >= start]
