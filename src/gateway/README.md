# gateway

Single entry point built on YARP: routing, JWT validation, a token bucket per device key and a fixed window per user, WebSocket forwarding to realtime, and the patient overview composition endpoint.

## Endpoints

- `GET /api/patients/{id}/overview` (composed from patients, vitals, alerts, risk-engine and devices)
- everything else under `/api/*` and `/hubs/*` is proxied; routes are in `appsettings.json`

## Events

- Publishes: none
- Consumes: none

## Run it alone

```bash
dotnet run --project src/gateway/RemoteHealthWatch.Gateway
```

Needs the other services at the cluster addresses in `appsettings.Development.json`. Port 5000.
