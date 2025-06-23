# How it works

This walks through the three flows that matter most: a reading travelling from a device to the dashboard, a patient
being enrolled, and an unacknowledged alert escalating. State diagrams for devices, enrollment and alerts follow.

## From device to dashboard

A wearable patch uploads a batch every few seconds. The batch carries a per-device sequence number on every reading
and the device's own timestamp.

```mermaid
sequenceDiagram
    autonumber
    participant D as Device
    participant G as gateway
    participant DV as devices
    participant K as Kafka
    participant V as vitals
    participant R as risk-engine
    participant A as alerts
    participant RT as realtime
    participant UI as Dashboard

    D->>G: POST /api/ingest/readings (X-Device-Key)
    G->>G: token bucket per key prefix
    G->>DV: forward
    DV->>DV: verify key hash (cache-aside), check paired, validate batch
    DV->>K: devices.readings-raw (key = patientId)
    DV-->>D: 202 Accepted {batchId}
    K->>V: readings-raw (consumer group "vitals")
    V->>V: normalise units, drop (deviceId, seq) duplicates, flag late readings
    V->>V: append to columnstore, recompute touched 1m/1h rollups, outbox row (one transaction)
    V->>V: update Redis latest snapshot (newer values only)
    V->>K: vitals.readings-normalized (outbox relay)
    par fan-out
        K->>R: readings-normalized
        R->>R: NEWS2 over last 60 min, z-score vs baseline, trend check
        R->>K: risk.score-updated / anomaly / trend (outbox)
    and
        K->>A: readings-normalized
        A->>A: threshold rules with the patient's own limits (gRPC, cached)
        A->>K: alerts.alert-raised (outbox)
    and
        K->>RT: readings-normalized
        RT->>UI: "vitals" to patient:{id} and the coordinators' group
    end
    K->>A: risk.score-updated (band went up)
    A->>K: alerts.alert-raised (News2BandChange)
    K->>RT: alerts.alert-raised
    RT->>UI: "alert" push (toast, inbox refresh)
```

What the dashboard shows is therefore a few hundred milliseconds behind the device. Locally, with 60 simulated devices,
alert events reached the browser about 40 ms after they were created (`docs/streaming-and-scaling.md` has the numbers).

### Example ingestion batch

```http
POST /api/ingest/readings
X-Device-Key: rhwd_3f9a1c2e_N7m...redacted...Q
Content-Type: application/json

{
  "batteryPercent": 81,
  "readings": [
    { "seq": 1749719400001, "vital": "HeartRate", "value": 88, "unit": "bpm", "measuredAt": "2025-06-12T09:10:00Z" },
    { "seq": 1749719400002, "vital": "SpO2", "value": 0.94, "unit": "fraction", "measuredAt": "2025-06-12T09:10:00Z" },
    { "seq": 1749719400003, "vital": "BloodPressure", "value": 132, "value2": 84, "unit": "mmHg", "measuredAt": "2025-06-12T09:10:00Z" }
  ]
}
```

`SpO2` arrives as a fraction here; vitals stores 94 %. Temperature in °F, glucose in mmol/L and blood pressure in kPa are
converted the same way (`src/services/vitals/RemoteHealthWatch.Vitals/Domain/UnitNormalizer.cs`).

## Enrollment (choreography saga)

No service orchestrates enrollment. Each step is a reaction to the previous step's event, and the patient's status in
`patients` is the saga state.

```mermaid
sequenceDiagram
    autonumber
    participant C as Coordinator
    participant P as patients
    participant K as Kafka
    participant D as devices
    participant A as alerts
    participant R as risk-engine

    C->>P: POST /api/patients (deviceSerial: RHW-PATCH-0007)
    P->>P: status PendingDevice, default thresholds by condition
    P->>K: patient-enrolled, care-team-changed (outbox)
    K->>R: patient-enrolled (SpO2 scale, oxygen)
    K->>D: patient-enrolled
    alt serial known and free
        D->>D: pair device with patient
        D->>K: device-paired
        K->>P: device-paired
        P->>P: PendingDevice -> BaselineBuilding
        Note over P: BaselineWatcher, 24h later
        P->>P: BaselineBuilding -> MonitoringActive
    else unknown, revoked or paired elsewhere
        D->>K: device-pairing-failed
        K->>P: stays PendingDevice, lastPairingError recorded
        K->>A: Info alert to the care coordinator
    end
```

### Compensation

If the last device is later unpaired (by a coordinator, or because it was revoked), `devices` publishes
`device-unpaired`; `patients` moves the patient back to `PendingDevice` and clears the baseline, and `alerts` raises an
Info alert for the coordinator. Discharging publishes `patient-discharged`, which unpairs every device (devices),
resolves every open alert (alerts) and stops scoring (risk-engine).

The demo seed enrolls one patient, "Orla Pending", with a serial that doesn't exist, so the failure path is visible
from the first minute.

## Alert escalation (orchestration / process manager)

Escalation is driven by one component that owns the alert's state: `EscalationManager` in alerts. Timeouts are Quartz
triggers stored in SQL Server, so they survive restarts and fire on exactly one replica.

```mermaid
sequenceDiagram
    autonumber
    participant A as alerts
    participant Q as Quartz (SQL job store)
    participant N as Nurse
    participant Dr as On-call doctor
    participant CC as Care coordinator

    A->>A: Raised -> NotifiedNurse (critical)
    A->>N: email + SMS
    A->>Q: trigger escalate-{id}-0 at +2 min
    Q-->>A: fires at +2 min
    A->>A: level still 0 and unacknowledged? -> EscalatedDoctor
    A->>Dr: email + SMS
    A->>Q: trigger escalate-{id}-1 at +5 min
    N->>A: POST /api/alerts/{id}/acknowledge
    A->>A: Acknowledged (version check wins the race)
    A->>Q: delete remaining triggers
    Q-->>A: a late trigger fires anyway
    A->>A: level mismatch -> no-op, nobody paged twice
```

| Severity | Nurse window | Doctor window | Coordinator window |
|---|---|---|---|
| Critical | 2 min | 5 min | 10 min |
| High | 5 min | 15 min | 30 min |
| Medium | 15 min | 30 min | 60 min |
| Low | 30 min | 60 min | 120 min |
| Info | goes straight to the coordinator, never escalates | | |

When the coordinator window also expires the alert is marked unanswered and everyone on the care team is paged once
more; the clock then stops.

## State diagrams

### Device

```mermaid
stateDiagram-v2
    [*] --> Available: registered (key issued once)
    Available --> Paired: pair (REST or enrollment saga)
    Paired --> Available: unpair / patient discharged
    Paired --> Paired: key rotated (old key valid 10 min)
    Paired --> Silent: no upload for 3 intervals (min 60 s)
    Silent --> Paired: next accepted batch
    Available --> Revoked: revoke
    Paired --> Revoked: revoke (also unpairs)
    Revoked --> [*]
```

`Silent` is a flag on a paired device (`SilentSince`), not a separate status column.

### Enrollment

```mermaid
stateDiagram-v2
    [*] --> PendingDevice: enrolled
    PendingDevice --> BaselineBuilding: device-paired
    PendingDevice --> PendingDevice: device-pairing-failed
    BaselineBuilding --> MonitoringActive: 24 h elapsed
    BaselineBuilding --> PendingDevice: last device unpaired
    MonitoringActive --> PendingDevice: last device unpaired
    PendingDevice --> Discharged: discharge
    BaselineBuilding --> Discharged: discharge
    MonitoringActive --> Discharged: discharge
    Discharged --> [*]
```

Readings are scored and checked against thresholds from the moment a device is paired; `BaselineBuilding` only means
the anomaly detector doesn't yet have a full day to compare against.

### Alert

```mermaid
stateDiagram-v2
    [*] --> Raised
    Raised --> NotifiedNurse: page nurse
    Raised --> Resolved: info alert closed
    NotifiedNurse --> EscalatedDoctor: nurse window expired
    EscalatedDoctor --> EscalatedCoordinator: doctor window expired
    EscalatedCoordinator --> EscalatedCoordinator: coordinator window expired (unanswered, page all)
    NotifiedNurse --> Acknowledged: ack
    EscalatedDoctor --> Acknowledged: ack
    EscalatedCoordinator --> Acknowledged: ack
    NotifiedNurse --> Resolved: resolve
    EscalatedDoctor --> Resolved: resolve
    EscalatedCoordinator --> Resolved: resolve
    Acknowledged --> Resolved: resolve / NEWS2 back to low / discharge
    Resolved --> [*]
```

While an alert is open, the same problem (same patient, type and metric) increments its occurrence count rather than
raising a new alert; a worse reading upgrades its severity and tightens the clock. After it is resolved, the same
problem at the same or lower severity is suppressed for a window (5 to 60 minutes by severity).
