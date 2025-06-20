# seed

Creates the synthetic programme through the gateway, exactly as a coordinator would: registers one patch per patient,
enrolls the patient with that serial (so pairing goes through the enrollment saga), adds a few spare devices and one
patient whose requested device doesn't exist. Writes `devices.json` with each device's key and scenario for the
simulator. Running it again keeps existing patients and reuses the keys it already wrote.

```bash
SEED_DEMO_PASSWORD=Demo@12345 GATEWAY_URL=http://localhost:5000/ dotnet run --project tools/seed/RemoteHealthWatch.Seed
```

| Variable | Default |
|---|---|
| `GATEWAY_URL` | `http://localhost:5000/` |
| `SEED_ADMIN_EMAIL` | `coordinator@remotehealthwatch.local` |
| `SEED_DEMO_PASSWORD` | (required) |
| `SEED_PATIENTS` | 60 |
| `SEED_SPARE_DEVICES` | 5 |
| `SEED_INTERVAL_SECONDS` | 5 |
| `SEED_OUT` | `tools/seed/out/devices.json` |
