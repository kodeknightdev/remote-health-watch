# Tech stack

Every version below is pinned in the repo (`global.json`, `Directory.Packages.props` with lock files, `requirements.txt`,
`package-lock.json`, image tags).

## .NET

| Component | Version | Used for |
|---|---|---|
| .NET SDK | 9.0.2xx/3xx (`global.json` pins 9.0.202 with feature-band roll-forward; images build with 9.0.301) | building every .NET service, the seed tool, the device simulator and the test projects |
| ASP.NET Core runtime image | `mcr.microsoft.com/dotnet/aspnet:9.0.6-bookworm-slim` | runtime image for identity, patients, devices, vitals, alerts, realtime, gateway, seed and device-sim (non-root `$APP_UID`) |
| C# | 13 | all .NET code |
| EF Core + SQL Server provider | 9.0.3 | data access and migrations in identity, patients, devices, vitals and alerts; raw SQL for the vitals columnstore and MERGE rollups |
| ASP.NET Core Identity (EF) | 9.0.3 | staff accounts, password hashing, lockout and roles in identity |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.3 | access-token validation in every .NET service and the gateway (shared defaults) |
| System.IdentityModel.Tokens.Jwt | 8.7.0 | issuing access tokens in identity; short-lived service tokens for service-to-service calls |
| Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore | 9.0.3 + 2.1.6 | OpenAPI document and Scalar UI (`/scalar/v1`) on every .NET HTTP service |
| SignalR + StackExchangeRedis backplane | 9.0.3 | the `/hubs/live` hub in realtime, shared across replicas through Redis |
| Microsoft.Extensions.Caching.Hybrid | 9.3.0 | cache-aside for thresholds and care teams (alerts), visible patients (alerts, realtime), device key lookups (devices) |
| Microsoft.Extensions.Caching.StackExchangeRedis / StackExchange.Redis | 9.0.2 / 2.8.31 | HybridCache L2; latest-vitals snapshot in vitals; processed-event markers in realtime |
| Grpc.AspNetCore.Server / Grpc.Net.ClientFactory | 2.70.0 | the `PatientDirectory` gRPC server in patients and its clients in alerts, vitals, devices and realtime |
| Google.Protobuf / Grpc.Tools | 3.30.2 / 2.67.0 | messages and code generation from `src/shared/protos/patients.proto` in the contracts project |
| Yarp.ReverseProxy | 2.3.0 | routing, WebSocket forwarding and per-route policies in the gateway |
| Confluent.Kafka | 2.9.0 | producing and consuming every Kafka topic: outbox relay, consumer host, retry and dead-letter lanes (shared defaults) and raw ingestion in devices |
| Microsoft.Extensions.Http.Resilience | 9.3.0 | retry, timeout and circuit breaker on the gRPC directory client, devices → patients, alerts → identity and the gateway's composition clients |
| Quartz.Extensions.Hosting + Serialization.SystemTextJson | 3.14.0 | durable, clustered escalation timers and the daily digest job in alerts |
| MailKit | 4.11.0 | escalation and digest emails from alerts (MailHog locally) |
| FluentValidation (+ DI extensions) | 11.11.0 | request validation in every .NET API, returned as ValidationProblemDetails |
| Serilog.AspNetCore | 9.0.0 | structured logging with the PHI-redaction enricher in every .NET service |
| AspNetCore.HealthChecks.SqlServer / Redis / Kafka | 9.0.0 | readiness checks behind `/health` |
| OpenTelemetry (Extensions.Hosting, OTLP exporter) | 1.11.2 | traces and metrics from every .NET service, including Kafka produce/consume spans |
| OpenTelemetry.Instrumentation.AspNetCore / Http / Runtime | 1.11.1 | HTTP server, HTTP/gRPC client and runtime instrumentation |

## Python

| Component | Version | Used for |
|---|---|---|
| Python | 3.13 (`python:3.13.3-slim-bookworm`) | risk-engine and insights |
| FastAPI | 0.115.12 | the HTTP APIs of risk-engine and insights |
| uvicorn | 0.34.2 | serving both FastAPI apps |
| pydantic / pydantic-settings | 2.11.3 / 2.9.1 | response models; `RISK_*` and `INSIGHTS_*` configuration |
| confluent-kafka | 2.10.0 | consumers, retry/DLQ lanes and the outbox relay in both Python services |
| SQLAlchemy / Alembic | 2.0.40 / 1.15.2 | data access and schema migrations for `rhw_risk` and `rhw_insights` |
| pyodbc + Microsoft ODBC Driver 18 | 5.2.0 + msodbcsql18 18.5.1.1 | connecting both Python services to SQL Server |
| numpy / pandas | 2.2.5 / 2.2.3 | baselines, minute pivots and trend regression in risk-engine |
| scikit-learn | 1.6.1 | per-patient IsolationForest anomaly detection in risk-engine |
| ReportLab | 4.4.0 | weekly patient PDF reports with charts in insights |
| azure-storage-blob | 12.25.1 | storing and fetching report PDFs (Azurite locally) in insights |
| PyJWT / httpx | 2.10.1 / 0.28.1 | validating access tokens, minting service tokens, REST calls to patients |
| OpenTelemetry SDK / OTLP gRPC exporter / FastAPI instrumentation | 1.32.1 / 1.32.1 / 0.53b1 | traces from both Python services, continued from Kafka headers |
| pytest | 8.3.5 | the risk-engine and insights test suites |

## Frontend

| Component | Version | Used for |
|---|---|---|
| Angular (core, router, forms, cdk) | 19.2.10 | the clinician dashboard: standalone components, signals, new control flow |
| Angular CLI / build | 19.2.11 | building and serving the dashboard, running its tests |
| Angular Material | 19.2.15 | tables, forms, sidenav, chips and snackbars in the dashboard |
| @microsoft/signalr | 8.0.7 | the dashboard's live connection to `/hubs/live` |
| ECharts / ngx-echarts | 5.6.0 / 19.0.0 | sparklines, trend charts and the programme KPI chart |
| TypeScript | 5.7.3 | all dashboard code |
| RxJS | 7.8.2 | HTTP calls and event streams in the dashboard |
| Jasmine / Karma | 5.6.0 / 6.4.4 | dashboard unit and component tests in headless Chrome |
| Node (build image) | 22.14.0 | building the dashboard image |
| nginx (runtime image) | 1.27.5-alpine | serving the dashboard and proxying `/api` and `/hubs` to the gateway |

## Data and messaging

| Component | Version | Used for |
|---|---|---|
| SQL Server | 2022 CU17 (`mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04`) | one database per service; the partitioned columnstore reading store in vitals; the Quartz job store in alerts |
| Apache Kafka | 3.9.0 (KRaft, `apache/kafka:3.9.0`) | every event stream between services, with retry and dead-letter topics |
| Kafka UI | provectuslabs/kafka-ui v0.7.2 | browsing topics, consumer lag and dead-letter messages locally |
| Redis | 7.4.2 (`redis:7.4.2-alpine`) | latest-vitals snapshot, HybridCache L2, SignalR backplane |
| Azurite | 3.34.0 | local blob storage for insights' report PDFs |
| MailHog | 1.0.1 | catching alert and digest emails locally |
| Jaeger | 1.66.0 all-in-one | collecting and viewing traces from all services locally |

## Testing

| Component | Version | Used for |
|---|---|---|
| xUnit / runner | 2.9.3 / 3.0.2 | all .NET test projects |
| Microsoft.NET.Test.Sdk | 17.13.0 | running the .NET tests |
| Shouldly | 4.3.0 | assertions in the .NET tests |
| NSubstitute | 5.3.0 | test doubles for schedulers and notifiers in the alerts tests |
| Testcontainers (MsSql, Redis) | 4.3.0 | integration tests for vitals and alerts against real SQL Server and Redis |
| EF Core InMemory | 9.0.3 | identity token tests and the escalation manager tests |
| Microsoft.Extensions.TimeProvider.Testing | 9.3.0 | controlling time in token, escalation and validation tests |
| coverlet.collector | 6.0.4 | code coverage in the pipeline |

## Infrastructure and delivery

| Component | Version / detail | Used for |
|---|---|---|
| Docker / Docker Compose | Compose spec with `include` (Compose v2.20+) | building every image and running the full local stack, seed and simulator |
| Kubernetes / Kustomize | `autoscaling/v2`, `policy/v1`, `networking.k8s.io/v1`; kubectl's built-in kustomize | the `deploy/k8s` base and the dev and prod overlays |
| KEDA (optional) | `keda.sh/v1alpha1` ScaledObject | optional lag-based scaling of vitals |
| ingress-nginx | WebSocket timeout and cookie affinity annotations | routing `/api`, `/hubs` and the dashboard in Kubernetes |
| Azure DevOps Pipelines | `Docker@2`, `KubernetesManifest@0`, `UseDotNet@2`, `UsePythonVersion@0`, `NodeTool@0` | build, test, scan, push images to ACR, deploy to AKS |

Cloud resources (AKS, ACR, Azure SQL, Event Hubs, Key Vault) are assumed to exist and are outside this repo.
