# devices

Device registry, hashed API keys (rotation with a 10-minute grace, revocation), pairing, the ingestion endpoint and silent-device detection. Ingestion validates and rate-limits each batch, then produces it to Kafka keyed by patient.

## Endpoints

- `POST /api/ingest/readings` (header `X-Device-Key`)
- `GET/POST /api/devices`, `GET /api/devices/{id}`, `GET /api/devices/patient/{patientId}`
- `POST /api/devices/{id}/pair`, `/unpair`, `/rotate-key`, `/revoke`, `PUT .../firmware`

## Events

- Publishes: `devices.readings-raw` (direct), `devices.device-paired`, `device-unpaired`, `device-pairing-failed`, `device-silent` (outbox)
- Consumes: `patients.patient-enrolled` (pairs the requested serial), `patients.patient-discharged` (unpairs)

## Run it alone

```bash
dotnet run --project src/services/devices/RemoteHealthWatch.Devices
```

Needs SQL Server (`rhw_devices`), Redis, Kafka, patients (REST + gRPC). Port 5103 in Development.
