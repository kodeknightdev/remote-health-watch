# device-sim

Simulates home devices posting reading batches to `/api/ingest/readings`. Each device follows its patient's scenario
(stable, sepsis-like deterioration, COPD, hypertensive, noisy sensor, dropout), keeps a monotonic sequence number,
buffers while offline and resends on reconnect, backs off on `429`, and reloads its key on `401`. Prints accepted
readings per second every 30 seconds.

```bash
dotnet run --project tools/device-sim/RemoteHealthWatch.DeviceSim
```

| Variable | Default |
|---|---|
| `GATEWAY_URL` | `http://localhost:5000/` |
| `SIM_DEVICES_FILE` | `tools/seed/out/devices.json` |
| `SIM_DEVICES` | 60 (up to 1000) |
| `SIM_INTERVAL_SECONDS` | the patient's plan interval |
| `SIM_DETERIORATION_MINUTES` | 30 |
| `SIM_DROPOUT_CYCLE_MINUTES` / `SIM_DROPOUT_MINUTES` | 10 / 3 |
