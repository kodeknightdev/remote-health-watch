# patients

Enrollment, monitoring plans, personal thresholds (condition defaults plus overrides), care teams and the PHI audit log. Holds the enrollment saga state and serves the `PatientDirectory` gRPC API used on other services' hot paths.

## Endpoints

- `GET/POST /api/patients`, `GET /api/patients/{id}`
- `GET/PUT /api/patients/{id}/thresholds`, `DELETE .../thresholds/{metric}`
- `PUT /api/patients/{id}/plan`, `/care-team`, `POST .../discharge`
- `GET /api/audit` (admins)
- internal: `/internal/patients/{id}/pairing-eligibility`, `/internal/patients/{id}/profile`, `/internal/users/{id}/patients`, `POST /internal/access`
- gRPC (port 8081): `GetThresholds`, `GetCareTeam`, `GetMonitoringProfile`, `GetPatientsForUser`, `AuthorizeAccess`

## Events

- Publishes: `patients.patient-enrolled`, `thresholds-changed`, `care-team-changed`, `patient-discharged`, `monitoring-status-changed` (outbox)
- Consumes: `devices.device-paired`, `device-unpaired`, `device-pairing-failed` (enrollment saga steps)

## Run it alone

```bash
dotnet run --project src/services/patients/RemoteHealthWatch.Patients
```

Needs SQL Server (`rhw_patients`), Kafka. REST on 5102, gRPC on 5112 in Development.
