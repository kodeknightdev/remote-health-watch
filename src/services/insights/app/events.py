"""Same envelope shape the .NET services use: camelCase, enums as strings, surrogate ids only."""

import json
import uuid
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from app.db import utcnow


@dataclass(frozen=True)
class Envelope:
    id: str
    type: str
    schema_version: int
    occurred_at: str
    source: str
    correlation_id: str | None
    data: dict[str, Any]

    @staticmethod
    def parse(raw: str | bytes) -> "Envelope":
        d = json.loads(raw)
        return Envelope(
            id=d["id"],
            type=d["type"],
            schema_version=d.get("schemaVersion", 1),
            occurred_at=d.get("occurredAt", ""),
            source=d.get("source", ""),
            correlation_id=d.get("correlationId"),
            data=d.get("data") or {},
        )


@dataclass(frozen=True)
class Outgoing:
    topic: str
    key: str
    event_type: str
    data: dict[str, Any]

    def envelope(self, source: str, at: datetime | None = None) -> tuple[str, str]:
        event_id = str(uuid.uuid4())
        body = {
            "id": event_id,
            "type": self.event_type,
            "schemaVersion": 1,
            "occurredAt": (at or utcnow()).isoformat(),
            "source": source,
            "data": self.data,
        }
        return event_id, json.dumps(body, separators=(",", ":"))


class Topics:
    READINGS_NORMALIZED = "vitals.readings-normalized"
    PATIENT_ENROLLED = "patients.patient-enrolled"
    PATIENT_DISCHARGED = "patients.patient-discharged"
    SCORE_UPDATED = "risk.score-updated"
    ALERT_RAISED = "alerts.alert-raised"
    ALERT_ACKNOWLEDGED = "alerts.acknowledged"
    ALERT_ESCALATED = "alerts.escalated"
    ALERT_RESOLVED = "alerts.resolved"
    REPORT_GENERATED = "insights.report-generated"
    RETRY_SUFFIX = ".retry"
    DLQ_SUFFIX = ".dlq"
