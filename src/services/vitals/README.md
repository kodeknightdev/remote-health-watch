# vitals

The write side and read models for readings. Drops `(deviceId, seq)` duplicates, flags late readings, normalises units, appends to a monthly-partitioned clustered columnstore, recomputes the touched 1-minute and 1-hour rollups and keeps the latest value per vital in Redis.

## Endpoints

- `GET /api/vitals/{patientId}/latest`, `/trends?vital=&range=24h|7d|30d`, `/summary?hours=`, `/readings`, `/devices`
- `GET /api/vitals/stats/throughput?minutes=`

## Events

- Publishes: `vitals.readings-normalized` (outbox)
- Consumes: `devices.readings-raw` (group `vitals`, scale replicas up to 12), `devices.device-paired`, `device-unpaired`, `device-silent`

## Run it alone

```bash
dotnet run --project src/services/vitals/RemoteHealthWatch.Vitals
```

Needs SQL Server (`rhw_vitals`), Redis, Kafka, patients gRPC. Port 5104 in Development.
