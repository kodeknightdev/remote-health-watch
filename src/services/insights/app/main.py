import logging
import threading
from contextlib import asynccontextmanager
from datetime import datetime, timedelta

from fastapi import FastAPI
from fastapi.responses import JSONResponse
from sqlalchemy import select, text

from app import db, kafka_io, logging_setup, reports, telemetry
from app.api import router
from app.auth import PatientsClient
from app.blob import AzureBlobStore
from app.config import Settings, get_settings
from app.events import Topics
from app.facts import FactHandlers
from app.models import Report

settings = get_settings()
logging_setup.configure(settings.service_name)
log = logging.getLogger("insights")


def next_weekly_run(now: datetime, weekday: int, hour: int) -> datetime:
    run = now.replace(hour=hour, minute=0, second=0, microsecond=0) + timedelta(days=(weekday - now.weekday()) % 7)
    return run if run > now else run + timedelta(days=7)


def weekly_reports(cfg: Settings, blobs: AzureBlobStore, stop: threading.Event) -> None:
    while True:
        now = db.utcnow()
        wait = (next_weekly_run(now, cfg.weekly_report_weekday, cfg.weekly_report_hour_utc) - now).total_seconds()
        if stop.wait(wait):
            return
        week = reports.week_start_for(db.utcnow().date()) - timedelta(days=7)
        try:
            with db.session() as s:
                ids = reports.patients_for_weekly_run(s, week)
            done = 0
            for patient_id in ids:
                with db.session() as s:
                    exists = s.execute(select(Report.report_id).where(Report.patient_id == patient_id, Report.week_start == week)).first()
                    if exists:
                        continue
                    _, event = reports.generate(s, blobs, cfg.blob_container, patient_id, week)
                    kafka_io.stage(s, [event], cfg.service_name)
                    done += 1
            log.info("weekly reports for week of %s: %s generated", week, done)
        except Exception as ex:
            log.warning("weekly report run failed: %s", ex)


@asynccontextmanager
async def lifespan(app: FastAPI):
    db.init(settings.database_url)
    app.state.patients = PatientsClient(settings)
    app.state.blobs = AzureBlobStore(settings.blob_connection_string, settings.blob_container)
    stop = threading.Event()
    workers: list = []

    if settings.kafka_enabled:
        producer = kafka_io.make_producer(settings.kafka_bootstrap, settings.service_name)
        app.state.producer = producer
        facts = FactHandlers()
        common = dict(bootstrap=settings.kafka_bootstrap, source=settings.service_name, producer=producer,
                      max_attempts=settings.max_delivery_attempts, base_delay=settings.retry_base_delay_seconds)
        lanes = [
            ("insights-patients", [Topics.PATIENT_ENROLLED, Topics.PATIENT_DISCHARGED], facts.patients),
            ("insights-readings", [Topics.READINGS_NORMALIZED], facts.readings),
            ("insights-scores", [Topics.SCORE_UPDATED], facts.scores),
            ("insights-alerts", [Topics.ALERT_RAISED, Topics.ALERT_ACKNOWLEDGED, Topics.ALERT_ESCALATED, Topics.ALERT_RESOLVED], facts.alerts),
        ]
        for group, topics, handler in lanes:
            for retry in (False, True):
                workers.append(kafka_io.ConsumerLoop(group=group, topics=topics, handler=handler, retry_lane=retry, **common))
        workers.append(kafka_io.OutboxRelay(producer))
        for w in workers:
            w.start()
    threading.Thread(target=weekly_reports, args=(settings, app.state.blobs, stop), daemon=True).start()
    log.info("insights started")
    yield
    stop.set()
    for w in workers:
        w.stop()


app = FastAPI(title="remote-health-watch insights", version="1.0.0", lifespan=lifespan, docs_url="/docs", openapi_url="/openapi/v1.json")
app.include_router(router)
telemetry.configure(settings.service_name, app)


@app.get("/alive", include_in_schema=False)
def alive():
    return {"status": "Healthy"}


@app.get("/health", include_in_schema=False)
def health():
    checks = {}
    try:
        with db.engine().connect() as c:
            c.execute(text("SELECT 1"))
        checks["sqlserver"] = "Healthy"
    except Exception as ex:
        checks["sqlserver"] = f"Unhealthy: {ex.__class__.__name__}"
    try:
        app.state.blobs.ping()
        checks["azurite"] = "Healthy"
    except Exception as ex:
        checks["azurite"] = f"Unhealthy: {ex.__class__.__name__}"
    if settings.kafka_enabled and hasattr(app.state, "producer"):
        try:
            app.state.producer.list_topics(timeout=3)
            checks["kafka"] = "Healthy"
        except Exception as ex:
            checks["kafka"] = f"Unhealthy: {ex.__class__.__name__}"
    healthy = all(v == "Healthy" for v in checks.values())
    return JSONResponse({"status": "Healthy" if healthy else "Unhealthy", "checks": checks}, status_code=200 if healthy else 503)
