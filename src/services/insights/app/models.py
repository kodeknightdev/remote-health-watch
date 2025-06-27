from datetime import date, datetime

from sqlalchemy import Date, DateTime, Float, Index, Integer, String, Text
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class PatientFact(Base):
    __tablename__ = "patients"

    patient_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    interval_seconds: Mapped[int] = mapped_column(Integer, default=60)
    enrolled_at: Mapped[datetime] = mapped_column(DateTime)
    discharged_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


class AlertFact(Base):
    __tablename__ = "alert_facts"
    __table_args__ = (
        Index("ix_alert_facts_raised", "raised_at"),
        Index("ix_alert_facts_patient", "patient_id", "raised_at"),
    )

    alert_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    patient_id: Mapped[str] = mapped_column(String(36))
    type: Mapped[str] = mapped_column(String(30), default="")
    severity: Mapped[str] = mapped_column(String(12), default="")
    raised_at: Mapped[datetime] = mapped_column(DateTime)
    acknowledged_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    seconds_to_ack: Mapped[float | None] = mapped_column(Float, nullable=True)
    escalations: Mapped[int] = mapped_column(Integer, default=0)
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


class HourlyVital(Base):
    """Hourly aggregate per vital, enough for weekly trend charts and wear time without asking vitals."""

    __tablename__ = "hourly_vitals"

    patient_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    vital: Mapped[str] = mapped_column(String(20), primary_key=True)
    hour: Mapped[datetime] = mapped_column(DateTime, primary_key=True)
    count: Mapped[int] = mapped_column(Integer, default=0)
    total: Mapped[float] = mapped_column(Float, default=0.0)
    min_value: Mapped[float] = mapped_column(Float)
    max_value: Mapped[float] = mapped_column(Float)


class DailyScore(Base):
    __tablename__ = "daily_scores"

    patient_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    day: Mapped[date] = mapped_column(Date, primary_key=True)
    max_score: Mapped[int] = mapped_column(Integer)
    last_band: Mapped[str] = mapped_column(String(12))


class Report(Base):
    __tablename__ = "reports"
    __table_args__ = (Index("ix_reports_patient_week", "patient_id", "week_start"),)

    report_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    patient_id: Mapped[str] = mapped_column(String(36))
    week_start: Mapped[date] = mapped_column(Date)
    container: Mapped[str] = mapped_column(String(60))
    blob_name: Mapped[str] = mapped_column(String(200))
    size_bytes: Mapped[int] = mapped_column(Integer)
    generated_at: Mapped[datetime] = mapped_column(DateTime)


class ProcessedEvent(Base):
    __tablename__ = "processed_events"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    consumer: Mapped[str] = mapped_column(String(60), primary_key=True)
    processed_at: Mapped[datetime] = mapped_column(DateTime)


class OutboxEvent(Base):
    __tablename__ = "outbox"
    __table_args__ = (Index("ix_outbox_pending", "published_at", "created_at"),)

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    topic: Mapped[str] = mapped_column(String(100))
    key: Mapped[str] = mapped_column(String(64))
    payload: Mapped[str] = mapped_column(Text)
    traceparent: Mapped[str | None] = mapped_column(String(100), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime)
    published_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    attempts: Mapped[int] = mapped_column(Integer, default=0)
