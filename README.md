# remote-health-watch

remote-health-watch is a remote patient monitoring platform for a hospital's home-care programme. Patients wear home devices that stream heart rate, SpO₂, blood pressure, respiratory rate, temperature and glucose; the platform ingests the streams in real time, scores each patient with NEWS2, spots deviations from their personal baseline, and gives clinicians a live dashboard with alerts that escalate on their own when nobody responds. It is built for nurses, doctors and care coordinators running a virtual ward, and as a reference for event-driven microservices on .NET. It's a demo only, All patients, devices and readings are synthetic. This is not a medical device and must not be used for clinical decisions.

## Features

**Enrollment**
- Enroll patients with conditions, a care team and a monitoring plan (which vitals, how often)
- Personal thresholds with condition defaults (e.g. a lower SpO₂ target for COPD) and clinician overrides
- Discharge from monitoring; every access to patient data is audited

**Devices**
- Device registry with type, serial, firmware, battery and last-seen status
- Per-device API keys, stored hashed, with rotation and revocation
- Pairing and unpairing; alerts when a device goes silent

**Live monitoring**
- Patient list sorted by risk with NEWS2 badges, live vital tiles and sparklines
- Patient detail with 24 h, 7 day and 30 day trends, thresholds and device status
- Care-team scoped: nurses and doctors only see their own patients

**Risk scoring**
- NEWS2 over a rolling window, including SpO₂ scale 2
- Anomalies against each patient's own baseline (rolling z-score, then IsolationForest after 24 h)
- Steady-deterioration trend detection

**Alerts and escalation**
- Threshold, NEWS2, anomaly, trend and connectivity alerts with severities
- Deduplication and suppression windows instead of alert storms
- Acknowledge, resolve and add notes
- Automatic escalation nurse → on-call doctor → care coordinator (critical: 2 → 5 → 10 min)
- Email and SMS notifications, daily digest for coordinators

**Reports**
- Weekly PDF report per patient (trends, alerts, wear-time compliance)
- Programme dashboard: enrolment, alerts per day, time to acknowledge, escalation rate, wear time

## Tech stack

.NET 9 · ASP.NET Core · EF Core · SignalR · gRPC · YARP · Kafka · SQL Server · Redis · Azurite · Quartz.NET · OpenTelemetry ·
Python · FastAPI · scikit-learn · ReportLab · Angular · Angular Material · ECharts · Docker · Kubernetes · Kustomize · Azure DevOps

## Getting started

Prerequisites: Docker Desktop (or Docker Engine) with Compose v2.20+ and about 8 GB of memory for containers.

```bash
cp .env.example .env
docker compose up --build
```

The first start builds every image and takes a few minutes. A one-shot seeder then enrolls the synthetic patients and
the device simulator starts streaming.

| What | URL |
|---|---|
| Dashboard | http://localhost:4200 |
| Gateway | http://localhost:5000 |
| API docs (Scalar) | http://localhost:5000/scalar/v1 and each service at `http://localhost:510x/scalar/v1` |
| Kafka UI | http://localhost:8085 |
| MailHog (notification emails) | http://localhost:8025 |
| Jaeger (traces) | http://localhost:16686 |

On macOS the AirPlay Receiver also listens on port 5000; set `GATEWAY_PORT` in `.env` to another port if the gateway
can't bind.

Scale the device simulator through `.env` or the command line:

```bash
SIM_DEVICES=60 SIM_INTERVAL_SECONDS=2 docker compose up -d device-sim     # faster uploads
SEED_PATIENTS=500 SIM_DEVICES=500 docker compose up -d seed device-sim    # about 500 devices
docker compose up -d --scale vitals=3 --no-recreate vitals                 # more consumers
```

## Demo data and logins

When the stack starts it fills itself with 60 made-up patients, each wearing a simulated device. Every patient follows a
simple story:

- some stay healthy
- a few slowly get sicker, a bit like early sepsis
- some have COPD and naturally low oxygen
- some have high blood pressure
- some have a glitchy sensor
- some lose their connection now and then

Log in with any of these accounts. The password for all of them is `Demo@12345`.

| Role | Email |
|---|---|
| Doctor | `doctor@remotehealthwatch.local` |
| Nurse | `nurse@remotehealthwatch.local` |
| Care Coordinator | `coordinator@remotehealthwatch.local` |
| Admin | `admin@remotehealthwatch.local` |

They only exist in the local demo; the password comes from `SEED_DEMO_PASSWORD` in `.env.example`.

## Project layout

```
src/gateway/            YARP gateway and the patient overview endpoint
src/services/           identity, patients, devices, vitals, alerts, realtime (.NET), risk-engine, insights (Python)
src/shared/             contracts, protos and shared service plumbing
angular-frontend/       clinician dashboard
tools/                  seeder and device simulator
tests/                  .NET test projects
deploy/                 docker compose, kafka topics, sql init, kubernetes, pipeline templates
```

## Running tests

```bash
dotnet test RemoteHealthWatch.sln                            # Testcontainers tests are skipped without Docker
(cd src/services/risk-engine && pip install -r requirements.txt && pytest)
(cd src/services/insights && pip install -r requirements.txt && pytest)
(cd angular-frontend && npm ci && npx ng test --watch=false --browsers=ChromeHeadlessCI)
```
