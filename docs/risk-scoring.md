# Risk scoring

The risk engine (`src/services/risk-engine`) turns the normalized reading stream into three signals:

1. a **NEWS2** score per patient, recomputed on every batch,
2. **anomalies** measured against the patient's own baseline,
3. a **deterioration trend** over several hours.

All three are published to Kafka; alerts turns them into alerts, realtime pushes them to the dashboard, and insights
folds the scores into weekly reports.

> This is a demo. The scoring follows the published NEWS2 chart but has not been validated for clinical use.

## NEWS2

NEWS2 is the Royal College of Physicians' National Early Warning Score 2 (2017). Each parameter scores 0 to 3 and the
total maps to a clinical response band. Implementation: [`app/news2.py`](../src/services/risk-engine/app/news2.py).

| Parameter | 3 | 2 | 1 | 0 | 1 | 2 | 3 |
|---|---|---|---|---|---|---|---|
| Respiration rate (/min) | ≤ 8 | | 9–11 | 12–20 | | 21–24 | ≥ 25 |
| SpO₂ scale 1 (%) | ≤ 91 | 92–93 | 94–95 | ≥ 96 | | | |
| SpO₂ scale 2 (%) | ≤ 83 | 84–85 | 86–87 | 88–92, or ≥ 93 on air | 93–94 on O₂ | 95–96 on O₂ | ≥ 97 on O₂ |
| Air or oxygen | | Oxygen | | Air | | | |
| Systolic BP (mmHg) | ≤ 90 | 91–100 | 101–110 | 111–219 | | | ≥ 220 |
| Pulse (/min) | ≤ 40 | | 41–50 | 51–90 | 91–110 | 111–130 | ≥ 131 |
| Consciousness | | | | Alert | | | New confusion, V, P or U |
| Temperature (°C) | ≤ 35.0 | | 35.1–36.0 | 36.1–38.0 | 38.1–39.0 | ≥ 39.1 | |

| Aggregate | Band | Alert severity raised |
|---|---|---|
| 0–4, no single 3 | Low | none (an open NEWS2 alert is resolved) |
| any single parameter scoring 3 | Low–medium | Medium |
| 5–6 | Medium | High |
| ≥ 7 | High | Critical |

**Rolling window.** For each parameter the engine uses the newest observation within the last 60 minutes
(`RISK_NEWS2_WINDOW_MINUTES`). Late readings are stored but can't displace a newer value, because "newest" is decided by
the device timestamp, not arrival order.

**Scale 2.** Patients enrolled with COPD default to SpO₂ scale 2 (the clinician can change it). Scale 2 is meant for
confirmed hypercapnic respiratory failure; in a real deployment that is a clinical decision, not a condition lookup.

**Publishing.** A score is published when the score or band changes, or at least every 5 minutes
(`RISK_SCORE_HEARTBEAT_MINUTES`) so the dashboard never shows a stale band. Each event carries the per-parameter
components and the previous band:

```json
{ "patientId": "8c1d7a52-...", "score": 11, "band": "High", "previousBand": "Low",
  "components": { "RespiratoryRate": 3, "SpO2": 2, "AirOrOxygen": 0, "Systolic": 2, "Pulse": 2, "Consciousness": 0, "Temperature": 2 },
  "computedAt": "2025-06-12T10:22:41+00:00" }
```

### Reference cases (from the tests)

| Case | Inputs | Score | Band |
|---|---|---|---|
| All normal | RR 16, SpO₂ 97, SBP 125, HR 72, T 36.8 | 0 | Low |
| Single red parameter | RR 7, rest normal | 3 | Low–medium |
| Medium | RR 22, SpO₂ 95, HR 105, T 38.4 | 5 | Medium |
| Septic picture | RR 26, SpO₂ 92, SBP 98, HR 124, T 39.2 | 11 | High |
| COPD on scale 2 | SpO₂ 89 on air | SpO₂ component 0 (3 on scale 1) | |
| Oxygen | any, on supplemental O₂ | +2 | |

Every row boundary in the chart above has a parametrised test in
[`tests/test_news2.py`](../src/services/risk-engine/tests/test_news2.py).

## Baselines and z-score anomalies

Thresholds catch values that are wrong for anyone; baselines catch values that are wrong for *this* patient. A COPD
patient who lives at 89 % is fine; the same patient dropping to 84 % within an hour is not, even before any threshold
fires.

Implementation: [`app/baseline.py`](../src/services/risk-engine/app/baseline.py),
[`app/engine.py`](../src/services/risk-engine/app/engine.py).

- **Window.** The last 24 h of observations, excluding the most recent 30 minutes so a developing problem doesn't
  become part of "normal".
- **Robustness.** The 2.5th and 97.5th percentiles are trimmed before taking mean and standard deviation, so a short
  motion artefact doesn't widen the baseline.
- **Standard deviation floor.** Some patients are remarkably steady; without a floor a 1 % SpO₂ wobble becomes a
  10-sigma event. Floors: HR 3, SpO₂ 0.8, RR 1.0, temperature 0.15, BP 4, glucose 8.
- **Minimum data.** No baseline until there are 30 samples.
- **Threshold.** |z| ≥ 3.5 (`RISK_ZSCORE_THRESHOLD`), checked on the newest non-late reading of each vital.
- **Cooldown.** One anomaly per vital per method per 15 minutes, before alerts' own dedup even starts.
- **Caching.** Baselines are stored in `baselines` and refreshed at most every 10 minutes per patient and vital.

## IsolationForest

Once a patient has 24 h of history (`RISK_IFOREST_MIN_HOURS`), the engine trains a scikit-learn `IsolationForest` on
one-minute vectors of HR, SpO₂, RR, temperature and systolic BP ([`app/anomaly.py`](../src/services/risk-engine/app/anomaly.py)).
It catches combinations the per-vital z-score misses: a heart rate and a respiratory rate that are each only a little
high, together.

- Training data: the baseline window, pivoted to one row per minute and forward-filled up to 30 minutes for slower
  sensors (BP every minute, the patch every few seconds).
- 150 trees, contamination 1 %, fixed random state. The decision threshold is the 0.5th percentile of training scores,
  capped at 0, so borderline minutes don't alert.
- Models live in memory per patient and retrain hourly; a restart simply retrains on the next batch.
- The alert names the feature furthest from its baseline, so it reads "RespiratoryRate unusual for this patient" rather
  than "multivariate anomaly".

The test [`tests/test_anomaly.py`](../src/services/risk-engine/tests/test_anomaly.py) trains on a stable synthetic day and
checks that a stable next half hour is not flagged while a sepsis-like vector is.

## Deterioration trend

Sepsis and respiratory decline often show up as a slow slide: respiration creeping up and saturation creeping down
over hours, each still inside its thresholds ([`app/trend.py`](../src/services/risk-engine/app/trend.py)).

- Every 10 minutes per patient, fit a least-squares line to minute-averaged RR and SpO₂ over the last 6 h
  (`RISK_TREND_WINDOW_HOURS`).
- Flag when RR rises at ≥ 0.8 breaths/min per hour **and** SpO₂ falls at ≥ 0.4 percentage points per hour.
- Both series need at least 30 points and must cover two thirds of the window.
- One flag per patient per 2 hours. Alerts raises it as High severity.

## Limitations

- Consciousness (AVPU / new confusion) can't come from a home device, so it is always scored as Alert. A real
  deployment would need a patient-reported or clinician-entered value.
- Supplemental oxygen is a static flag on the monitoring plan, not live data.
- Device readings are noisier than nurse observations; NEWS2 was validated on the latter.
- Baselines adapt: a patient who deteriorates very slowly over days drags their baseline along. The trend rule and
  absolute thresholds are the guard against that.
- The IsolationForest has no labels; it can only say "unusual", not "dangerous".
- Models are per patient and in memory, so a horizontally scaled risk engine would train the same model on every
  replica that owns a patient's partition after a rebalance. Fine at this scale.
