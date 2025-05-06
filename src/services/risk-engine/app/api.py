import json
from datetime import datetime, timedelta

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import BaseModel
from sqlalchemy import func, select

from app import db
from app.auth import PatientsClient, User, current_user
from app.models import AnomalyRow, RiskScore, TrendFlag

router = APIRouter(prefix="/api/risk", tags=["risk"])


class ScoreOut(BaseModel):
    patientId: str
    score: int
    band: str
    components: dict[str, int]
    complete: bool
    computedAt: datetime


class AnomalyOut(BaseModel):
    vital: str
    value: float
    zScore: float | None
    method: str
    measuredAt: datetime
    detectedAt: datetime


class TrendOut(BaseModel):
    respiratoryRateSlopePerHour: float
    spO2SlopePerHour: float
    detectedAt: datetime


def patients_client(request: Request) -> PatientsClient:
    return request.app.state.patients


def require_access(patient_id: str, resource: str, user: User, patients: PatientsClient) -> None:
    if user.sees_all and user.role == "Service":
        return
    if not patients.can_access(user, patient_id, "read", resource):
        raise HTTPException(403, "patient is not on your care team")


def _score_out(row: RiskScore) -> ScoreOut:
    return ScoreOut(patientId=row.patient_id, score=row.score, band=row.band, components=json.loads(row.components),
                    complete=row.complete, computedAt=db.aware(row.computed_at))


@router.get("/scores", response_model=list[ScoreOut])
def current_scores(user: User = Depends(current_user), patients: PatientsClient = Depends(patients_client)):
    """Latest score for every patient the caller can see; the dashboard sorts its list by this."""
    visible = patients.visible_patients(user)
    with db.session() as s:
        latest = (
            select(RiskScore.patient_id, func.max(RiskScore.computed_at).label("at"))
            .group_by(RiskScore.patient_id).subquery()
        )
        rows = s.execute(
            select(RiskScore).join(latest, (RiskScore.patient_id == latest.c.patient_id)
                                   & (RiskScore.computed_at == latest.c.at))
        ).scalars().all()
    return [_score_out(r) for r in rows if visible is None or r.patient_id.lower() in visible]


@router.get("/{patient_id}/current", response_model=ScoreOut)
def current(patient_id: str, user: User = Depends(current_user), patients: PatientsClient = Depends(patients_client)):
    require_access(patient_id, "risk.current", user, patients)
    with db.session() as s:
        row = s.execute(select(RiskScore).where(RiskScore.patient_id == patient_id)
                        .order_by(RiskScore.computed_at.desc()).limit(1)).scalar_one_or_none()
    if row is None:
        raise HTTPException(404, "no score yet")
    return _score_out(row)


@router.get("/{patient_id}/history", response_model=list[ScoreOut])
def history(patient_id: str, hours: int = Query(24, ge=1, le=720), user: User = Depends(current_user),
            patients: PatientsClient = Depends(patients_client)):
    require_access(patient_id, "risk.history", user, patients)
    since = db.naive(db.utcnow() - timedelta(hours=hours))
    with db.session() as s:
        rows = s.execute(select(RiskScore).where(RiskScore.patient_id == patient_id, RiskScore.computed_at >= since)
                         .order_by(RiskScore.computed_at)).scalars().all()
    return [_score_out(r) for r in rows]


@router.get("/{patient_id}/anomalies", response_model=list[AnomalyOut])
def anomalies(patient_id: str, hours: int = Query(24, ge=1, le=720), user: User = Depends(current_user),
              patients: PatientsClient = Depends(patients_client)):
    require_access(patient_id, "risk.anomalies", user, patients)
    since = db.naive(db.utcnow() - timedelta(hours=hours))
    with db.session() as s:
        rows = s.execute(select(AnomalyRow).where(AnomalyRow.patient_id == patient_id, AnomalyRow.detected_at >= since)
                         .order_by(AnomalyRow.detected_at.desc())).scalars().all()
    return [AnomalyOut(vital=r.vital, value=r.value, zScore=r.zscore, method=r.method,
                       measuredAt=db.aware(r.measured_at), detectedAt=db.aware(r.detected_at)) for r in rows]


@router.get("/{patient_id}/trends", response_model=list[TrendOut])
def trends(patient_id: str, hours: int = Query(72, ge=1, le=720), user: User = Depends(current_user),
           patients: PatientsClient = Depends(patients_client)):
    require_access(patient_id, "risk.trends", user, patients)
    since = db.naive(db.utcnow() - timedelta(hours=hours))
    with db.session() as s:
        rows = s.execute(select(TrendFlag).where(TrendFlag.patient_id == patient_id, TrendFlag.detected_at >= since)
                         .order_by(TrendFlag.detected_at.desc())).scalars().all()
    return [TrendOut(respiratoryRateSlopePerHour=r.rr_slope, spO2SlopePerHour=r.spo2_slope,
                     detectedAt=db.aware(r.detected_at)) for r in rows]
