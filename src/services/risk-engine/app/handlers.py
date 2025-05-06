import logging

from sqlalchemy.orm import Session

from app.auth import PatientsClient
from app.db import utcnow
from app.engine import RiskEngine
from app.events import Envelope, Outgoing, Topics

log = logging.getLogger(__name__)


class RiskHandlers:
    def __init__(self, engine: RiskEngine, patients: PatientsClient):
        self.engine = engine
        self.patients = patients

    def readings(self, s: Session, envelope: Envelope, topic: str) -> list[Outgoing]:
        return self.engine.process(s, envelope.data, utcnow())

    def patient_events(self, s: Session, envelope: Envelope, topic: str) -> list[Outgoing]:
        data = envelope.data
        patient_id = data["patientId"]
        if topic == Topics.PATIENT_ENROLLED:
            self.engine.upsert_profile(s, patient_id, spo2_scale=data.get("spO2Scale", 1),
                                       on_oxygen=data.get("onSupplementalOxygen", False),
                                       conditions=data.get("conditions", []), active=True, now=utcnow())
        elif topic == Topics.THRESHOLDS_CHANGED:
            profile = self.patients.profile(patient_id)
            if profile:
                self.engine.upsert_profile(s, patient_id, spo2_scale=profile.get("spO2Scale", 1),
                                           on_oxygen=profile.get("onSupplementalOxygen", False),
                                           conditions=profile.get("conditions", []),
                                           active=profile.get("status") != "Discharged", now=utcnow())
        elif topic == Topics.PATIENT_DISCHARGED:
            self.engine.deactivate(s, patient_id, utcnow())
        return []
