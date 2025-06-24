# risk-engine (Python)

NEWS2 over a rolling 60-minute window (with SpO₂ scale 2 and supplemental oxygen), per-patient baselines with rolling z-scores, an IsolationForest per patient once 24 h of data exist, and a deterioration-trend rule over 6 h.

## Endpoints

- `GET /api/risk/scores`
- `GET /api/risk/{patientId}/current`, `/history`, `/anomalies`, `/trends`
- `GET /health`, `/alive`, OpenAPI at `/openapi/v1.json`, UI at `/docs`

## Events

- Publishes: `risk.score-updated`, `risk.anomaly-detected`, `risk.deterioration-trend` (outbox)
- Consumes: `vitals.readings-normalized`, `patients.patient-enrolled`, `thresholds-changed`, `patient-discharged`

## Run it alone

```bash
pip install -r requirements.txt && alembic upgrade head && uvicorn app.main:app --port 5107
```

Needs SQL Server (`rhw_risk`) via ODBC Driver 18, Kafka, patients REST. Settings are `RISK_*` environment variables (`app/config.py`). Tests: `pytest`.
