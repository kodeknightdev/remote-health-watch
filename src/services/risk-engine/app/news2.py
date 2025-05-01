"""NEWS2 (Royal College of Physicians, 2017) aggregate scoring."""

from dataclasses import dataclass, field
from enum import Enum


class Band(str, Enum):
    LOW = "Low"
    LOW_MEDIUM = "LowMedium"
    MEDIUM = "Medium"
    HIGH = "High"


@dataclass(frozen=True)
class Observation:
    respiratory_rate: float | None = None
    spo2: float | None = None
    on_oxygen: bool = False
    systolic: float | None = None
    pulse: float | None = None
    # we only get device data, so consciousness defaults to Alert unless a clinician says otherwise
    consciousness_alert: bool = True
    temperature: float | None = None


@dataclass
class News2Result:
    score: int
    band: Band
    components: dict[str, int] = field(default_factory=dict)
    complete: bool = True


def score_respiratory_rate(rr: float) -> int:
    rr = round(rr)
    if rr <= 8:
        return 3
    if rr <= 11:
        return 1
    if rr <= 20:
        return 0
    if rr <= 24:
        return 2
    return 3


def score_spo2_scale1(spo2: float) -> int:
    spo2 = round(spo2)
    if spo2 <= 91:
        return 3
    if spo2 <= 93:
        return 2
    if spo2 <= 95:
        return 1
    return 0


def score_spo2_scale2(spo2: float, on_oxygen: bool) -> int:
    """Scale 2 for hypercapnic respiratory failure: target 88-92%, and high saturations on oxygen score too."""
    spo2 = round(spo2)
    if spo2 <= 83:
        return 3
    if spo2 <= 85:
        return 2
    if spo2 <= 87:
        return 1
    if spo2 <= 92 or not on_oxygen:
        return 0
    if spo2 <= 94:
        return 1
    if spo2 <= 96:
        return 2
    return 3


def score_systolic(sbp: float) -> int:
    sbp = round(sbp)
    if sbp <= 90:
        return 3
    if sbp <= 100:
        return 2
    if sbp <= 110:
        return 1
    if sbp <= 219:
        return 0
    return 3


def score_pulse(hr: float) -> int:
    hr = round(hr)
    if hr <= 40:
        return 3
    if hr <= 50:
        return 1
    if hr <= 90:
        return 0
    if hr <= 110:
        return 1
    if hr <= 130:
        return 2
    return 3


def score_temperature(temp: float) -> int:
    temp = round(temp, 1)
    if temp <= 35.0:
        return 3
    if temp <= 36.0:
        return 1
    if temp <= 38.0:
        return 0
    if temp <= 39.0:
        return 1
    return 2


def band_for(score: int, components: dict[str, int]) -> Band:
    if score >= 7:
        return Band.HIGH
    if score >= 5:
        return Band.MEDIUM
    # a single red parameter escalates even when the total is low
    if any(v == 3 for v in components.values()):
        return Band.LOW_MEDIUM
    return Band.LOW


def compute(obs: Observation, spo2_scale: int = 1) -> News2Result:
    components: dict[str, int] = {}
    if obs.respiratory_rate is not None:
        components["RespiratoryRate"] = score_respiratory_rate(obs.respiratory_rate)
    if obs.spo2 is not None:
        components["SpO2"] = (
            score_spo2_scale2(obs.spo2, obs.on_oxygen) if spo2_scale == 2 else score_spo2_scale1(obs.spo2)
        )
    components["AirOrOxygen"] = 2 if obs.on_oxygen else 0
    if obs.systolic is not None:
        components["Systolic"] = score_systolic(obs.systolic)
    if obs.pulse is not None:
        components["Pulse"] = score_pulse(obs.pulse)
    components["Consciousness"] = 0 if obs.consciousness_alert else 3
    if obs.temperature is not None:
        components["Temperature"] = score_temperature(obs.temperature)

    total = sum(components.values())
    complete = all(
        v is not None for v in (obs.respiratory_rate, obs.spo2, obs.systolic, obs.pulse, obs.temperature)
    )
    return News2Result(score=total, band=band_for(total, components), components=components, complete=complete)
