from datetime import date, datetime

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from fastapi.responses import Response
from pydantic import BaseModel
from sqlalchemy import select

from app import db, kafka_io, kpis, reports
from app.auth import PatientsClient, User, current_user
from app.config import get_settings
from app.models import Report

router = APIRouter(prefix="/api/insights", tags=["insights"])


class DayCount(BaseModel):
    day: date
    count: int


class KpisOut(BaseModel):
    enrolledPatients: int
    activePatients: int
    alertsPerDay: list[DayCount]
    averageMinutesToAcknowledge: float | None
    escalationRate: float
    wearTimeCompliance: float | None
    generatedAt: datetime


class ReportOut(BaseModel):
    reportId: str
    patientId: str
    weekStart: date
    blobName: str
    sizeBytes: int
    generatedAt: datetime


def _patients(request: Request) -> PatientsClient:
    return request.app.state.patients


def _require_programme(user: User) -> None:
    if user.role not in ("CareCoordinator", "Admin", "Service"):
        raise HTTPException(403, "programme metrics are for coordinators and admins")


def _require_patient(user: User, patient_id: str, resource: str, patients: PatientsClient) -> None:
    if not patients.can_access(user, patient_id, "read", resource):
        raise HTTPException(403, "patient is not on your care team")


def _out(r: Report) -> ReportOut:
    return ReportOut(
        reportId=r.report_id,
        patientId=r.patient_id,
        weekStart=r.week_start,
        blobName=r.blob_name,
        sizeBytes=r.size_bytes,
        generatedAt=db.aware(r.generated_at),
    )


@router.get("/kpis", response_model=KpisOut)
def get_kpis(days: int = Query(14, ge=1, le=90), user: User = Depends(current_user)):
    _require_programme(user)
    now = db.utcnow()
    with db.session() as s:
        k = kpis.compute(s, days, now)
    return KpisOut(
        enrolledPatients=k.enrolled_patients,
        activePatients=k.active_patients,
        alertsPerDay=[DayCount(day=d, count=c) for d, c in k.alerts_per_day],
        averageMinutesToAcknowledge=k.average_minutes_to_acknowledge,
        escalationRate=k.escalation_rate,
        wearTimeCompliance=k.wear_time_compliance,
        generatedAt=now,
    )


@router.get("/reports/patient/{patient_id}", response_model=list[ReportOut])
def list_reports(patient_id: str, user: User = Depends(current_user), patients: PatientsClient = Depends(_patients)):
    _require_patient(user, patient_id, "reports", patients)
    with db.session() as s:
        rows = (
            s.execute(select(Report).where(Report.patient_id == patient_id).order_by(Report.generated_at.desc()))
            .scalars()
            .all()
        )
    return [_out(r) for r in rows]


@router.post("/reports/patient/{patient_id}", response_model=ReportOut, status_code=201)
def generate_report(
    patient_id: str, request: Request, week_start: date | None = None, user: User = Depends(current_user)
):
    _require_programme(user)
    settings = get_settings()
    week = reports.week_start_for(week_start or db.utcnow().date())
    with db.session() as s:
        report, event = reports.generate(s, request.app.state.blobs, settings.blob_container, patient_id, week)
        kafka_io.stage(s, [event], settings.service_name)
    return _out(report)


@router.get("/reports/{report_id}/pdf")
def download(
    report_id: str, request: Request, user: User = Depends(current_user), patients: PatientsClient = Depends(_patients)
):
    with db.session() as s:
        report = s.get(Report, report_id)
    if report is None:
        raise HTTPException(404, "report not found")
    _require_patient(user, report.patient_id, "reports.pdf", patients)
    content = request.app.state.blobs.get(report.blob_name)
    return Response(
        content,
        media_type="application/pdf",
        headers={"Content-Disposition": f'attachment; filename="remote-health-watch-{report.week_start}.pdf"'},
    )
