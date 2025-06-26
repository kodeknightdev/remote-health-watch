# Development

## Project structure

```
remote-health-watch/
├── src/
│   ├── gateway/RemoteHealthWatch.Gateway/        YARP routes, rate limits, patient overview composition
│   ├── services/
│   │   ├── identity/                      users, JWT, rotating refresh tokens
│   │   ├── patients/                      enrollment saga state, thresholds, care teams, PHI audit, gRPC directory
│   │   ├── devices/                       registry, device keys, ingestion endpoint, silent-device sweep
│   │   ├── vitals/                        dedup, normalisation, columnstore store, rollups, Redis snapshot
│   │   ├── alerts/                        threshold rules, dedup/suppression, escalation process manager, paging
│   │   ├── realtime/                      SignalR hub fed from Kafka
│   │   ├── risk-engine/                   Python: NEWS2, baselines, IsolationForest, trends
│   │   └── insights/                      Python: KPIs, weekly PDF reports
│   └── shared/
│       ├── RemoteHealthWatch.Contracts/          event envelope, topics, integration events, generated gRPC code
│       ├── RemoteHealthWatch.ServiceDefaults/    logging + PHI redaction, auth, health, telemetry, Kafka/outbox, caching
│       └── protos/                        patients.proto
├── angular-frontend/                      Angular 19 dashboard (core/ services, shared/ widgets, features/ pages)
├── tests/                                 xUnit projects per service plus TestSupport and Platform tests
├── tools/
│   ├── seed/                              enrolls synthetic patients through the API, writes device keys
│   └── device-sim/                        simulated devices with clinical scenarios
├── deploy/
│   ├── docker-compose.yml (+ override)    the local stack
│   ├── kafka/                             topic catalog and creation script
│   ├── sqlserver/                         per-service databases and logins
│   ├── k8s/                               Kustomize base, dev/prod overlays, optional KEDA
│   └── pipelines/templates/               Azure DevOps job templates
├── compose.yaml                           includes deploy/ so `docker compose up` works from the root
├── azure-pipelines.yml
├── global.json, Directory.Build.props, Directory.Packages.props
└── docs/
```

Each .NET service follows the same layout: `Controllers/` (HTTP), `Domain/` (pure rules, unit tested),
`Services/` (application logic), `Messaging/` (Kafka handlers), `Data/` (DbContext and migrations), `Program.cs`.

## Running services without Docker

Infrastructure is easiest from compose even when the services run on the host:

```bash
cp .env.example .env
docker compose up -d sqlserver sqlserver-init kafka kafka-init redis azurite mailhog jaeger
```

The compose override publishes SQL Server on 1433, Kafka on 29092, Redis on 6379 and Azurite on 10000. Each service's
`appsettings.Development.json` points at those, with its own login from `.env.example`. Kafka on the host is
`localhost:29092`:

```bash
export Kafka__BootstrapServers=localhost:29092
dotnet run --project src/services/identity/RemoteHealthWatch.Identity      # :5101
dotnet run --project src/services/patients/RemoteHealthWatch.Patients      # :5102, gRPC :5112
dotnet run --project src/services/devices/RemoteHealthWatch.Devices        # :5103
dotnet run --project src/services/vitals/RemoteHealthWatch.Vitals          # :5104
dotnet run --project src/services/alerts/RemoteHealthWatch.Alerts          # :5105
dotnet run --project src/services/realtime/RemoteHealthWatch.Realtime      # :5106
dotnet run --project src/gateway/RemoteHealthWatch.Gateway                 # :5000
```

Python services:

```bash
cd src/services/risk-engine
python3.13 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
export RISK_DATABASE_URL="mssql+pyodbc://rhw_risk:Risk_dev_Pass1@localhost:1433/rhw_risk?driver=ODBC+Driver+18+for+SQL+Server&TrustServerCertificate=yes"
export RISK_KAFKA_BOOTSTRAP=localhost:29092 RISK_JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0123456789
alembic upgrade head
uvicorn app.main:app --port 5107 --reload
```

(The host needs Microsoft ODBC Driver 18; `RISK_KAFKA_ENABLED=false` runs the API without consumers.)

Dashboard with live reload against the gateway on :5000:

```bash
cd angular-frontend && npm ci && npm start     # http://localhost:4200, proxy.conf.json forwards /api and /hubs
```

Seed and simulate from the host:

```bash
SEED_DEMO_PASSWORD=Demo@12345 dotnet run --project tools/seed/RemoteHealthWatch.Seed
dotnet run --project tools/device-sim/RemoteHealthWatch.DeviceSim
```

## Simulator scenarios

The seeder assigns scenarios in a repeating block of ten, so every nurse's share of patients includes each kind.

| Scenario | Share | What it does | What you should see |
|---|---|---|---|
| Stable | 30 % | personal baseline with gentle drift | NEWS2 0–2, no alerts |
| Deteriorating (sepsis-like) | 10 % | over `SIM_DETERIORATION_MINUTES` (30): HR +45, RR +12, temp +2.4 °C, SpO₂ −7, SBP −35 | NEWS2 climbs through Medium to High; band alerts escalate if unacknowledged; deterioration trend after 6 h of slide |
| COPD | 20 % | SpO₂ 88–91 %, RR 18–21 | quiet with COPD thresholds and SpO₂ scale 2, unlike on default limits |
| Hypertensive | 20 % | SBP 158–172, DBP 94–102 | occasional warning breaches against the raised hypertension limits |
| Noisy sensor | 10 % | 4 % of samples are motion artefacts (HR 165–190, SpO₂ 72–82, RR 32–40) | one-off breaches merged and suppressed rather than an alert storm |
| Dropout | 10 % | offline 3 min in every 10, buffers, then sends current data first, the backlog, and a repeat of the last batch | device-silent alerts; late readings stored, duplicates dropped |

Every patch sends HR, SpO₂, RR and temperature each tick, blood pressure once a minute, and glucose every 120 ticks.
Devices whose serial ends in 3 report SpO₂ as a fraction to exercise normalisation.

| Variable | Default | Meaning |
|---|---|---|
| `SIM_DEVICES` | 60 | devices per simulator replica (needs at least that many seeded) |
| `SIM_REPLICAS` | 1 | compose replicas of `device-sim` |
| `SIM_INTERVAL_SECONDS` | 5 | upload interval |
| `SIM_DETERIORATION_MINUTES` | 30 | time for the sepsis-like slide |
| `SIM_DROPOUT_CYCLE_MINUTES` / `SIM_DROPOUT_MINUTES` | 10 / 3 | dropout pattern |
| `SEED_PATIENTS` | 60 | patients and devices the seeder creates |
| `BASELINE_HOURS` | 24 | baseline period before `MonitoringActive` |

## Tests

| Location | What | Run |
|---|---|---|
| `tests/RemoteHealthWatch.Identity.Tests` | token issue, rotation, reuse detection, expiry | `dotnet test` |
| `tests/RemoteHealthWatch.Patients.Tests` | care-team access rules, default thresholds, threshold validation, enrollment saga | |
| `tests/RemoteHealthWatch.Devices.Tests` | key hashing, rotation grace, revocation, silence rule, batch validation | |
| `tests/RemoteHealthWatch.Vitals.Tests` | unit normalisation, dedup and late readings; **Testcontainers** SQL Server + Redis: dedup on replay, partition placement, rollup recompute, snapshot ordering | |
| `tests/RemoteHealthWatch.Alerts.Tests` | threshold rules, dedup/suppression, escalation state machine, restart mid-escalation, ack racing a timeout, recovery sweep; **Testcontainers** SQL Server: migrations incl. Quartz, filtered unique index, merge and suppression | |
| `tests/RemoteHealthWatch.Platform.Tests` | PHI redaction in logs, event envelope, gateway device partitioning, simulator scenarios | |
| `src/services/risk-engine/tests` | NEWS2 reference cases for every boundary, baselines, IsolationForest, trend, engine behaviour | `pytest` |
| `src/services/insights/tests` | KPI arithmetic, fact folding, PDF + claim-check event, scheduling | `pytest` |
| `angular-frontend/src/**/*.spec.ts` | auth refresh sharing, interceptor retry, live store, risk sorting, NEWS2 badge, alert inbox | `npx ng test --watch=false --browsers=ChromeHeadlessCI` |

Testcontainers tests are marked `[DockerFact]` and are skipped (not failed) when no Docker daemon is reachable, or when
`SKIP_DOCKER_TESTS=1`. They start `mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04` and `redis:7.4.2-alpine`.

When the .NET SDK itself runs in a container, give it the host's Docker socket:

```bash
docker run --rm -v "$PWD":/src -w /src -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway \
  mcr.microsoft.com/dotnet/sdk:9.0.301-bookworm-slim dotnet test RemoteHealthWatch.sln
```

## Conventions

- **Central package management.** Versions live only in `Directory.Packages.props`; restores use lock files
  (`--locked-mode` in CI and Docker).
- **Events.** New integration events implement `IIntegrationEvent` with a stable `EventType` and `SchemaVersion`, are
  published through `IEventOutbox`, and carry surrogate ids only. Bump the schema version for breaking changes and keep
  consumers tolerant of unknown fields.
- **Handlers** implement `IEventHandler`, are registered with `.Consume<T>(group, topics)`, and must be idempotent; the
  host already dedupes on event id.
- **Domain rules are pure.** State machines and evaluators (`EscalationStateMachine`, `EnrollmentStateMachine`,
  `ThresholdEvaluator`, `news2.py`) take inputs and return decisions; IO stays in services and handlers.
- **PHI.** Never log names, DOB, MRN or contact details; the Serilog enricher and the Python formatter redact the common
  cases, but don't rely on them. Patient-scoped endpoints go through `[PatientData]` (or the access check) so they are
  audited.
- **Time** comes from `TimeProvider` (`FakeTimeProvider` in tests).
- **Comments** explain why, not what.
- **Commits** use conventional prefixes: `feat(scope):`, `fix(scope):`, `test:`, `docs:`, `build:`, `ci:`.

## Migrations

```bash
dotnet tool install --global dotnet-ef --version 9.0.3
dotnet ef migrations add AddSomething -p src/services/alerts/RemoteHealthWatch.Alerts -o Data/Migrations
```

Services apply their migrations on startup in Development (alerts always, because Quartz needs its tables before the
scheduler starts). For the Python services: `alembic revision -m "..."` in the service folder, then `alembic upgrade head`
(the container runs it on start).
