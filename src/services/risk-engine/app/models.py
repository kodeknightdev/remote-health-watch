from datetime import datetime

from sqlalchemy import BigInteger, Boolean, DateTime, Float, Index, Integer, String, Text
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class PatientProfile(Base):
    __tablename__ = "patient_profiles"

    patient_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    spo2_scale: Mapped[int] = mapped_column(Integer, default=1)
    on_oxygen: Mapped[bool] = mapped_column(Boolean, default=False)
    conditions: Mapped[str] = mapped_column(String(200), default="")
    active: Mapped[bool] = mapped_column(Boolean, default=True)
    updated_at: Mapped[datetime] = mapped_column(DateTime)


class Observation(Base):
    """Rolling copy of the last day and a half of readings; vitals owns the long-term store."""

    __tablename__ = "observations"
    __table_args__ = (Index("ix_observations_patient_vital_time", "patient_id", "vital", "measured_at"),)

    id: Mapped[int] = mapped_column(BigInteger().with_variant(Integer, "sqlite"), primary_key=True, autoincrement=True)
    patient_id: Mapped[str] = mapped_column(String(36))
    vital: Mapped[str] = mapped_column(String(20))
    value: Mapped[float] = mapped_column(Float)
    value2: Mapped[float | None] = mapped_column(Float, nullable=True)
    measured_at: Mapped[datetime] = mapped_column(DateTime)


class BaselineRow(Base):
    __tablename__ = "baselines"

    patient_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    vital: Mapped[str] = mapped_column(String(20), primary_key=True)
    mean: Mapped[float] = mapped_column(Float)
    std: Mapped[float] = mapped_column(Float)
    samples: Mapped[int] = mapped_column(Integer)
    computed_at: Mapped[datetime] = mapped_column(DateTime)


class RiskScore(Base):
    __tablename__ = "risk_scores"
    __table_args__ = (Index("ix_risk_scores_patient_time", "patient_id", "computed_at"),)

    id: Mapped[int] = mapped_column(BigInteger().with_variant(Integer, "sqlite"), primary_key=True, autoincrement=True)
    patient_id: Mapped[str] = mapped_column(String(36))
    score: Mapped[int] = mapped_column(Integer)
    band: Mapped[str] = mapped_column(String(12))
    components: Mapped[str] = mapped_column(Text)
    complete: Mapped[bool] = mapped_column(Boolean)
    computed_at: Mapped[datetime] = mapped_column(DateTime)


class AnomalyRow(Base):
    __tablename__ = "anomalies"
    __table_args__ = (Index("ix_anomalies_patient_time", "patient_id", "detected_at"),)

    id: Mapped[int] = mapped_column(BigInteger().with_variant(Integer, "sqlite"), primary_key=True, autoincrement=True)
    patient_id: Mapped[str] = mapped_column(String(36))
    vital: Mapped[str] = mapped_column(String(20))
    value: Mapped[float] = mapped_column(Float)
    zscore: Mapped[float | None] = mapped_column(Float, nullable=True)
    method: Mapped[str] = mapped_column(String(20))
    measured_at: Mapped[datetime] = mapped_column(DateTime)
    detected_at: Mapped[datetime] = mapped_column(DateTime)


class TrendFlag(Base):
    __tablename__ = "trend_flags"
    __table_args__ = (Index("ix_trend_flags_patient_time", "patient_id", "detected_at"),)

    id: Mapped[int] = mapped_column(BigInteger().with_variant(Integer, "sqlite"), primary_key=True, autoincrement=True)
    patient_id: Mapped[str] = mapped_column(String(36))
    rr_slope: Mapped[float] = mapped_column(Float)
    spo2_slope: Mapped[float] = mapped_column(Float)
    detected_at: Mapped[datetime] = mapped_column(DateTime)


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
