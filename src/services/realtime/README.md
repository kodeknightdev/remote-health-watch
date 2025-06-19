# realtime

The SignalR hub behind the live dashboard. Consumes Kafka and pushes to `patient:{id}` groups (care-team members) and the `all` group (coordinators, admins). A Redis backplane lets several replicas share clients.

## Endpoints

- hub `/hubs/live` (token in `access_token` query string)
- server → client: `vitals`, `risk`, `alert`, `insight`
- client → server: `Watch(patientId)`, `Unwatch(patientId)`

## Events

- Publishes: none
- Consumes: `vitals.readings-normalized`, `risk.score-updated`, `risk.anomaly-detected`, `risk.deterioration-trend`, `alerts.*`, `patients.care-team-changed`

## Run it alone

```bash
dotnet run --project src/services/realtime/RemoteHealthWatch.Realtime
```

Needs Redis, Kafka, patients gRPC. Port 5106 in Development.
