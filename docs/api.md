# API

Everything goes through the gateway: `http://localhost:5000` in Docker Compose (or the port in `GATEWAY_PORT`), and
`/api` on the dashboard's own origin (`http://localhost:4200`). All endpoints except login, refresh and device ingestion
need `Authorization: Bearer <access token>`. Errors are RFC 9457 problem details with a `traceId`.

## OpenAPI per service

In Development every service serves its OpenAPI document and a UI. With the compose override's host ports:

| Service | OpenAPI document | UI |
|---|---|---|
| identity | http://localhost:5101/openapi/v1.json | http://localhost:5101/scalar/v1 |
| patients | http://localhost:5102/openapi/v1.json | http://localhost:5102/scalar/v1 |
| devices | http://localhost:5103/openapi/v1.json | http://localhost:5103/scalar/v1 |
| vitals (first replica) | http://localhost:5114/openapi/v1.json | http://localhost:5114/scalar/v1 |
| alerts | http://localhost:5105/openapi/v1.json | http://localhost:5105/scalar/v1 |
| realtime | http://localhost:5106/openapi/v1.json | http://localhost:5106/scalar/v1 |
| gateway (overview) | http://localhost:5000/openapi/v1.json | http://localhost:5000/scalar/v1 |
| risk-engine | http://localhost:5107/openapi/v1.json | http://localhost:5107/docs |
| insights | http://localhost:5108/openapi/v1.json | http://localhost:5108/docs |

## Authentication (identity)

| Method | Path | Who | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anyone | `{ "email", "password" }` → access token (10 min) in the body, refresh token as an `HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/auth`. 20 attempts/min per client; 5 failures lock the account for 10 min. |
| POST | `/api/auth/refresh` | cookie | Rotates the refresh token. Presenting an already-used token revokes the whole token family. |
| POST | `/api/auth/logout` | cookie | Revokes the family and clears the cookie. |
| GET | `/api/auth/me` | staff | Current user. |
| GET | `/api/users?role=Nurse` | coordinator, admin | Staff directory for building care teams. |
| POST / PUT | `/api/users`, `/api/users/{id}` | admin | Create or update staff (role, on-call flag, active). |

```bash
curl -s -X POST http://localhost:5000/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"coordinator@remotehealthwatch.local","password":"Demo@12345"}'
```

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2025-06-12T09:20:00+00:00",
  "user": { "id": "3c9f...", "email": "coordinator@remotehealthwatch.local", "displayName": "Priya Desai", "role": "CareCoordinator", "isOnCall": false }
}
```

The access token carries `sub` (user id), `role`, `name` and `email`. Doctors and nurses only ever see patients on their
care team; coordinators and admins see the whole programme.

## Patients

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/patients?status=&search=` | staff | Care-team filtered list. Discharged patients only with `status=Discharged`. |
| GET | `/api/patients/{id}` | care team | Demographics, plan, thresholds, care team. Audited. |
| GET | `/api/patients/{id}/overview` | care team | **Composition** in the gateway (see below). |
| POST | `/api/patients` | coordinator, admin | Enroll. Optional `deviceSerial` starts the pairing saga. |
| GET / PUT | `/api/patients/{id}/thresholds` | care team | Personal limits per metric. PUT marks them `Override`. |
| DELETE | `/api/patients/{id}/thresholds/{metric}` | care team | Back to the condition default. |
| PUT | `/api/patients/{id}/plan` | care team | Vitals, interval, SpO₂ scale, oxygen. |
| PUT | `/api/patients/{id}/care-team` | coordinator, admin | |
| POST | `/api/patients/{id}/discharge` | coordinator, admin | |
| GET | `/api/patients/stats` | staff | Counts by status. |
| GET | `/api/audit?patientId=&userId=&deniedOnly=` | admin | PHI access log. |

Enrollment request:

```json
{
  "firstName": "Agnes", "lastName": "Holloway", "dateOfBirth": "1948-03-07", "sex": "F",
  "conditions": ["COPD"],
  "plan": { "vitals": ["HeartRate", "SpO2", "RespiratoryRate", "Temperature", "BloodPressure"], "intervalSeconds": 60 },
  "careTeam": [
    { "userId": "6b0e...", "role": "Nurse", "displayName": "Sam Okafor", "email": "nurse@remotehealthwatch.local", "isPrimary": true }
  ],
  "onSupplementalOxygen": false,
  "deviceSerial": "RHW-SPARE-001"
}
```

The response is `201` with the patient in `PendingDevice`; a second later it is `BaselineBuilding` if the device paired.

Thresholds update:

```json
{ "thresholds": [ { "metric": "SpO2", "lowCritical": 85, "lowWarning": 88, "highWarning": null, "highCritical": null } ] }
```

Metrics are `HeartRate`, `SpO2`, `RespiratoryRate`, `Temperature`, `Systolic`, `Diastolic`, `Glucose`. Values must
satisfy `lowCritical ≤ lowWarning < highWarning ≤ highCritical`.

### Patient overview (API composition)

`GET /api/patients/{id}/overview` returns:

```json
{
  "patientId": "8c1d...",
  "patient": { "...": "as GET /api/patients/{id}" },
  "latestVitals": [ { "vital": "SpO2", "value": 89, "unit": "%", "measuredAt": "...", "deviceId": "..." } ],
  "summary24h": { "vitals": [ { "vital": "HeartRate", "count": 17280, "min": 61, "max": 104, "avg": 78.2, "unit": "bpm" } ] },
  "openAlerts": [ { "id": "...", "severity": "Critical", "status": "EscalatedDoctor", "message": "SpO2 below critical limit (83 vs 84)" } ],
  "risk": { "score": 7, "band": "High", "components": { "...": 0 }, "computedAt": "..." },
  "devices": [ { "serial": "RHW-PATCH-0007", "status": "Paired", "batteryPercent": 81, "lastSeenAt": "..." } ],
  "degraded": []
}
```

If a downstream service is down, its section is `null`, its name appears in `degraded` and in the
`X-Composition-Degraded` header, and the response is still `200`.

## Devices and ingestion

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/devices?status=&silent=true` | coordinator, admin | Registry. |
| GET | `/api/devices/patient/{patientId}` | care team | Devices for one patient. |
| POST | `/api/devices` | coordinator, admin | Register; the API key is in this response only. |
| POST | `/api/devices/{id}/pair` | coordinator, admin | `{ "patientId", "expectedIntervalSeconds" }`; checks eligibility with patients. |
| POST | `/api/devices/{id}/unpair` | coordinator, admin | |
| POST | `/api/devices/{id}/rotate-key` | coordinator, admin | New key; the previous one keeps working for 10 minutes. |
| POST | `/api/devices/{id}/revoke` | coordinator, admin | Unpairs and rejects every key. |
| POST | `/api/ingest/readings` | device key | See contract below. |

### Ingestion contract

```http
POST /api/ingest/readings
X-Device-Key: rhwd_3f9a1c2e_<secret>
Content-Type: application/json
```

```json
{
  "batteryPercent": 81,
  "readings": [
    { "seq": 1749719400001, "vital": "HeartRate",       "value": 88,   "unit": "bpm",      "measuredAt": "2025-06-12T09:10:00Z" },
    { "seq": 1749719400002, "vital": "SpO2",            "value": 0.94, "unit": "fraction", "measuredAt": "2025-06-12T09:10:00Z" },
    { "seq": 1749719400003, "vital": "RespiratoryRate", "value": 18,   "unit": "/min",     "measuredAt": "2025-06-12T09:10:00Z" },
    { "seq": 1749719400004, "vital": "Temperature",     "value": 99.1, "unit": "F",        "measuredAt": "2025-06-12T09:10:00Z" },
    { "seq": 1749719400005, "vital": "BloodPressure",   "value": 132,  "value2": 84, "unit": "mmHg", "measuredAt": "2025-06-12T09:10:00Z" }
  ]
}
```

| Vital | Accepted units | Stored as |
|---|---|---|
| HeartRate | `bpm` | bpm |
| SpO2 | `%`, `fraction` | % |
| RespiratoryRate | `/min`, `brpm` | /min |
| Temperature | `C`, `F` | °C |
| BloodPressure | `mmHg`, `kPa` (systolic in `value`, diastolic in `value2`) | mmHg |
| Glucose | `mg/dL`, `mmol/L` | mg/dL |

Rules: `seq` positive and unique within the batch (and monotonic per device across batches), at most 500 readings,
`measuredAt` not more than 2 minutes ahead or 7 days behind.

| Response | Meaning |
|---|---|
| `202 { "batchId", "accepted" }` | On Kafka. `X-Device-Key-Rotated: true` if an old key inside its grace period was used. |
| `400` | Validation problem details. |
| `401` | Unknown or revoked key. |
| `409` | Device not paired with a patient. |
| `429` + `Retry-After` | Device over its token bucket. Keep the batch and retry. |

## Vitals

| Method | Path | Notes |
|---|---|---|
| GET | `/api/vitals/{patientId}/latest` | Redis snapshot, newest value per vital. |
| GET | `/api/vitals/{patientId}/trends?vital=HeartRate&range=24h` | `24h` → 1-minute points, `7d`/`30d` → 1-hour points; each point has avg, min, max (and avg2 for diastolic). |
| GET | `/api/vitals/{patientId}/summary?hours=24` | Count, min, avg, max per vital. |
| GET | `/api/vitals/{patientId}/readings?vital=&from=&to=` | Raw readings, newest first, max 2000. |
| GET | `/api/vitals/{patientId}/devices` | Device state as seen by vitals. |
| GET | `/api/vitals/stats/throughput?minutes=5` | Stored readings per second (coordinators, admins). |

All patient routes check the care team over gRPC and are audited.

## Alerts

| Method | Path | Notes |
|---|---|---|
| GET | `/api/alerts?status=unacknowledged\|open\|resolved\|all&minSeverity=` | Inbox for the caller's patients, unacknowledged first. |
| GET | `/api/alerts/{id}` | Alert with escalation timeline and notes. |
| GET | `/api/alerts/patient/{patientId}?openOnly=true` | |
| POST | `/api/alerts/{id}/acknowledge` | `409` if already acknowledged or resolved. |
| POST | `/api/alerts/{id}/resolve` | `{ "reason": "sensor repositioned" }` |
| POST | `/api/alerts/{id}/notes` | `{ "text": "..." }` |
| GET | `/api/alerts/stats?days=7` | Raised, acknowledged, escalated, average time to acknowledge (coordinators, admins). |

## Risk engine

| Method | Path | Notes |
|---|---|---|
| GET | `/api/risk/scores` | Latest score for every visible patient. |
| GET | `/api/risk/{patientId}/current` | Latest score with components; `404` before the first reading. |
| GET | `/api/risk/{patientId}/history?hours=24` | |
| GET | `/api/risk/{patientId}/anomalies?hours=24` | z-score and IsolationForest detections. |
| GET | `/api/risk/{patientId}/trends?hours=72` | Deterioration trend flags. |

## Insights

| Method | Path | Notes |
|---|---|---|
| GET | `/api/insights/kpis?days=14` | Enrolled and active patients, alerts per day, average minutes to acknowledge, escalation rate, wear-time compliance (coordinators, admins). |
| GET | `/api/insights/reports/patient/{patientId}` | Weekly report index. |
| POST | `/api/insights/reports/patient/{patientId}?week_start=2025-06-09` | Generate now (coordinators, admins). |
| GET | `/api/insights/reports/{reportId}/pdf` | Streams the PDF from blob storage. |

## gRPC: `remotehealthwatch.patients.v1.PatientDirectory`

Defined in [`src/shared/protos/patients.proto`](../src/shared/protos/patients.proto), served by patients on port 8081
(h2c), called with a short-lived service token.

| RPC | Used by | Purpose |
|---|---|---|
| `GetThresholds(PatientRequest) → ThresholdSet` | alerts | personal limits for threshold rules |
| `GetCareTeam(PatientRequest) → CareTeam` | alerts | who to page |
| `GetMonitoringProfile(PatientRequest) → MonitoringProfile` | (available) | status, conditions, SpO₂ scale |
| `GetPatientsForUser(UserRequest) → PatientIdList` | realtime, alerts | care-team filtering of lists and hub groups |
| `AuthorizeAccess(AccessRequest) → AccessDecision` | vitals, alerts, devices, realtime | care-team check and PHI audit entry in one call |

## SignalR hub: `/hubs/live`

Connect with `?access_token=<jwt>` (browsers can't set headers on the WebSocket upgrade). On connect the server adds the
connection to `patient:{id}` groups for the caller's care team, or to `all` for coordinators and admins.

| Server → client | Payload |
|---|---|
| `vitals` | `{ patientId, readings: [{ vital, value, value2, unit, measuredAt }] }` |
| `risk` | `{ patientId, score, band, previousBand, components, computedAt }` |
| `alert` | `{ kind: raised\|acknowledged\|escalated\|resolved, alertId, patientId, severity, type, status, message, occurredAt, pushedAt }` |
| `insight` | `{ kind: anomaly\|trend, patientId, summary, at }` |

| Client → server | Returns |
|---|---|
| `Watch(patientId)` | `true` if allowed (audited); joins the patient's group |
| `Unwatch(patientId)` | |

Enums are sent as names, same as the REST APIs.
