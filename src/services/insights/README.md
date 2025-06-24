# insights (Python)

Programme KPIs and weekly per-patient PDF reports. Folds alert, score and reading events into small fact tables; reports are rendered with ReportLab, stored in blob storage and announced with a claim-check event.

## Endpoints

- `GET /api/insights/kpis?days=`
- `GET /api/insights/reports/patient/{patientId}`, `POST` to generate now
- `GET /api/insights/reports/{reportId}/pdf`
- `GET /health`, `/alive`, OpenAPI at `/openapi/v1.json`, UI at `/docs`

## Events

- Publishes: `insights.report-generated` (outbox; carries the blob name, not the PDF)
- Consumes: `alerts.*`, `risk.score-updated`, `vitals.readings-normalized`, `patients.patient-enrolled`, `patient-discharged`

## Run it alone

```bash
pip install -r requirements.txt && alembic upgrade head && uvicorn app.main:app --port 5108
```

Needs SQL Server (`rhw_insights`), Azurite or Azure Blob Storage, Kafka, patients REST. Settings are `INSIGHTS_*` variables. Weekly reports run Monday 06:00 UTC. Tests: `pytest`.
