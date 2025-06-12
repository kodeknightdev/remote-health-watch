# alerts

Turns readings and risk signals into alerts, dedupes and suppresses them, and runs the escalation process manager (nurse → doctor → coordinator) on durable Quartz timers stored in its own database. Sends email (MailHog) and logged SMS, plus a daily digest for coordinators.

## Endpoints

- `GET /api/alerts?status=`, `GET /api/alerts/{id}`, `GET /api/alerts/patient/{patientId}`
- `POST /api/alerts/{id}/acknowledge`, `/resolve`, `/notes`
- `GET /api/alerts/stats?days=`

## Events

- Publishes: `alerts.alert-raised`, `alerts.acknowledged`, `alerts.escalated`, `alerts.resolved` (outbox)
- Consumes: `vitals.readings-normalized` (threshold rules), `risk.score-updated`, `risk.anomaly-detected`, `risk.deterioration-trend`, `devices.device-silent`, `device-paired`, `device-unpaired`, `device-pairing-failed`, `patients.thresholds-changed`, `care-team-changed`, `patient-discharged`

## Run it alone

```bash
dotnet run --project src/services/alerts/RemoteHealthWatch.Alerts
```

Needs SQL Server (`rhw_alerts`, including the Quartz tables), Redis, Kafka, patients gRPC, identity, SMTP. Port 5105 in Development.
