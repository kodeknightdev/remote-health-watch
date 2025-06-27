import logging
import threading
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.responses import JSONResponse
from sqlalchemy import text

from app import db, kafka_io, logging_setup, telemetry
from app.api import router
from app.auth import PatientsClient
from app.config import get_settings
from app.engine import RiskEngine
from app.events import Topics
from app.handlers import RiskHandlers

settings = get_settings()
logging_setup.configure(settings.service_name)
log = logging.getLogger("risk-engine")


def _purge_loop(engine: RiskEngine, stop: threading.Event) -> None:
    while not stop.wait(3600):
        try:
            with db.session() as s:
                removed = engine.purge(s, db.utcnow())
            log.info("purged %s old observations", removed)
        except Exception as ex:
            log.warning("purge failed: %s", ex)


@asynccontextmanager
async def lifespan(app: FastAPI):
    db.init(settings.database_url)
    patients = PatientsClient(settings)
    engine = RiskEngine(settings)
    handlers = RiskHandlers(engine, patients)
    app.state.patients = patients
    stop = threading.Event()
    workers: list = []

    if settings.kafka_enabled:
        producer = kafka_io.make_producer(settings.kafka_bootstrap, settings.service_name)
        app.state.producer = producer
        common = dict(
            bootstrap=settings.kafka_bootstrap,
            source=settings.service_name,
            producer=producer,
            max_attempts=settings.max_delivery_attempts,
            base_delay=settings.retry_base_delay_seconds,
        )
        for retry in (False, True):
            workers.append(
                kafka_io.ConsumerLoop(
                    group=settings.consumer_group,
                    topics=[Topics.READINGS_NORMALIZED],
                    handler=handlers.readings,
                    retry_lane=retry,
                    **common,
                )
            )
            workers.append(
                kafka_io.ConsumerLoop(
                    group=f"{settings.consumer_group}-patients",
                    topics=[Topics.PATIENT_ENROLLED, Topics.THRESHOLDS_CHANGED, Topics.PATIENT_DISCHARGED],
                    handler=handlers.patient_events,
                    retry_lane=retry,
                    **common,
                )
            )
        workers.append(kafka_io.OutboxRelay(producer))
        for w in workers:
            w.start()
    threading.Thread(target=_purge_loop, args=(engine, stop), daemon=True).start()
    log.info("risk engine started")
    yield
    stop.set()
    for w in workers:
        w.stop()


app = FastAPI(
    title="remote-health-watch risk engine",
    version="1.0.0",
    lifespan=lifespan,
    docs_url="/docs",
    openapi_url="/openapi/v1.json",
)
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
    if settings.kafka_enabled and hasattr(app.state, "producer"):
        try:
            app.state.producer.list_topics(timeout=3)
            checks["kafka"] = "Healthy"
        except Exception as ex:
            checks["kafka"] = f"Unhealthy: {ex.__class__.__name__}"
    healthy = all(v == "Healthy" for v in checks.values())
    return JSONResponse(
        {"status": "Healthy" if healthy else "Unhealthy", "checks": checks}, status_code=200 if healthy else 503
    )
