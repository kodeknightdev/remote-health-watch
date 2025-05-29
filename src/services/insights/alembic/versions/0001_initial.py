"""initial insights schema

Revision ID: 0001
Revises:
Create Date: 2025-05-29 10:00:00
"""
import sqlalchemy as sa
from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "patients",
        sa.Column("patient_id", sa.String(36), primary_key=True),
        sa.Column("interval_seconds", sa.Integer, nullable=False),
        sa.Column("enrolled_at", sa.DateTime, nullable=False),
        sa.Column("discharged_at", sa.DateTime, nullable=True),
    )
    op.create_table(
        "alert_facts",
        sa.Column("alert_id", sa.String(36), primary_key=True),
        sa.Column("patient_id", sa.String(36), nullable=False),
        sa.Column("type", sa.String(30), nullable=False),
        sa.Column("severity", sa.String(12), nullable=False),
        sa.Column("raised_at", sa.DateTime, nullable=False),
        sa.Column("acknowledged_at", sa.DateTime, nullable=True),
        sa.Column("seconds_to_ack", sa.Float, nullable=True),
        sa.Column("escalations", sa.Integer, nullable=False),
        sa.Column("resolved_at", sa.DateTime, nullable=True),
    )
    op.create_index("ix_alert_facts_raised", "alert_facts", ["raised_at"])
    op.create_index("ix_alert_facts_patient", "alert_facts", ["patient_id", "raised_at"])
    op.create_table(
        "hourly_vitals",
        sa.Column("patient_id", sa.String(36), primary_key=True),
        sa.Column("vital", sa.String(20), primary_key=True),
        sa.Column("hour", sa.DateTime, primary_key=True),
        sa.Column("count", sa.Integer, nullable=False),
        sa.Column("total", sa.Float, nullable=False),
        sa.Column("min_value", sa.Float, nullable=False),
        sa.Column("max_value", sa.Float, nullable=False),
    )
    op.create_table(
        "daily_scores",
        sa.Column("patient_id", sa.String(36), primary_key=True),
        sa.Column("day", sa.Date, primary_key=True),
        sa.Column("max_score", sa.Integer, nullable=False),
        sa.Column("last_band", sa.String(12), nullable=False),
    )
    op.create_table(
        "reports",
        sa.Column("report_id", sa.String(36), primary_key=True),
        sa.Column("patient_id", sa.String(36), nullable=False),
        sa.Column("week_start", sa.Date, nullable=False),
        sa.Column("container", sa.String(60), nullable=False),
        sa.Column("blob_name", sa.String(200), nullable=False),
        sa.Column("size_bytes", sa.Integer, nullable=False),
        sa.Column("generated_at", sa.DateTime, nullable=False),
    )
    op.create_index("ix_reports_patient_week", "reports", ["patient_id", "week_start"])
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
    for table in ("outbox", "processed_events", "reports", "daily_scores", "hourly_vitals", "alert_facts", "patients"):
        op.drop_table(table)
