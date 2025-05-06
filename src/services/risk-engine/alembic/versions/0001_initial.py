"""initial risk engine schema

Revision ID: 0001
Revises:
Create Date: 2025-05-01 10:00:00
"""
import sqlalchemy as sa
from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "patient_profiles",
        sa.Column("patient_id", sa.String(36), primary_key=True),
        sa.Column("spo2_scale", sa.Integer, nullable=False),
        sa.Column("on_oxygen", sa.Boolean, nullable=False),
        sa.Column("conditions", sa.String(200), nullable=False),
        sa.Column("active", sa.Boolean, nullable=False),
        sa.Column("updated_at", sa.DateTime, nullable=False),
    )
    op.create_table(
        "observations",
        sa.Column("id", sa.BigInteger, primary_key=True, autoincrement=True),
        sa.Column("patient_id", sa.String(36), nullable=False),
        sa.Column("vital", sa.String(20), nullable=False),
        sa.Column("value", sa.Float, nullable=False),
        sa.Column("value2", sa.Float, nullable=True),
        sa.Column("measured_at", sa.DateTime, nullable=False),
    )
    op.create_index("ix_observations_patient_vital_time", "observations", ["patient_id", "vital", "measured_at"])
    op.create_table(
        "baselines",
        sa.Column("patient_id", sa.String(36), primary_key=True),
        sa.Column("vital", sa.String(20), primary_key=True),
        sa.Column("mean", sa.Float, nullable=False),
        sa.Column("std", sa.Float, nullable=False),
        sa.Column("samples", sa.Integer, nullable=False),
        sa.Column("computed_at", sa.DateTime, nullable=False),
    )
    op.create_table(
        "risk_scores",
        sa.Column("id", sa.BigInteger, primary_key=True, autoincrement=True),
        sa.Column("patient_id", sa.String(36), nullable=False),
        sa.Column("score", sa.Integer, nullable=False),
        sa.Column("band", sa.String(12), nullable=False),
        sa.Column("components", sa.Text, nullable=False),
        sa.Column("complete", sa.Boolean, nullable=False),
        sa.Column("computed_at", sa.DateTime, nullable=False),
    )
    op.create_index("ix_risk_scores_patient_time", "risk_scores", ["patient_id", "computed_at"])
    op.create_table(
        "anomalies",
        sa.Column("id", sa.BigInteger, primary_key=True, autoincrement=True),
        sa.Column("patient_id", sa.String(36), nullable=False),
        sa.Column("vital", sa.String(20), nullable=False),
        sa.Column("value", sa.Float, nullable=False),
        sa.Column("zscore", sa.Float, nullable=True),
        sa.Column("method", sa.String(20), nullable=False),
        sa.Column("measured_at", sa.DateTime, nullable=False),
        sa.Column("detected_at", sa.DateTime, nullable=False),
    )
    op.create_index("ix_anomalies_patient_time", "anomalies", ["patient_id", "detected_at"])
    op.create_table(
        "trend_flags",
        sa.Column("id", sa.BigInteger, primary_key=True, autoincrement=True),
        sa.Column("patient_id", sa.String(36), nullable=False),
        sa.Column("rr_slope", sa.Float, nullable=False),
        sa.Column("spo2_slope", sa.Float, nullable=False),
        sa.Column("detected_at", sa.DateTime, nullable=False),
    )
    op.create_index("ix_trend_flags_patient_time", "trend_flags", ["patient_id", "detected_at"])
    op.create_table(
        "processed_events",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("consumer", sa.String(60), primary_key=True),
        sa.Column("processed_at", sa.DateTime, nullable=False),
    )
    op.create_table(
        "outbox",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("topic", sa.String(100), nullable=False),
        sa.Column("key", sa.String(64), nullable=False),
        sa.Column("payload", sa.Text, nullable=False),
        sa.Column("traceparent", sa.String(100), nullable=True),
        sa.Column("created_at", sa.DateTime, nullable=False),
        sa.Column("published_at", sa.DateTime, nullable=True),
        sa.Column("attempts", sa.Integer, nullable=False),
    )
    op.create_index("ix_outbox_pending", "outbox", ["published_at", "created_at"])


def downgrade() -> None:
    for table in ("outbox", "processed_events", "trend_flags", "anomalies", "risk_scores", "baselines",
                  "observations", "patient_profiles"):
        op.drop_table(table)
