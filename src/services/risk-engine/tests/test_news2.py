import pytest

from app import news2
from app.news2 import Band, Observation


@pytest.mark.parametrize("rr,expected", [(8, 3), (9, 1), (11, 1), (12, 0), (20, 0), (21, 2), (24, 2), (25, 3)])
def test_respiratory_rate_bands(rr, expected):
    assert news2.score_respiratory_rate(rr) == expected


@pytest.mark.parametrize("spo2,expected", [(91, 3), (92, 2), (93, 2), (94, 1), (95, 1), (96, 0), (100, 0)])
def test_spo2_scale_1(spo2, expected):
    assert news2.score_spo2_scale1(spo2) == expected


@pytest.mark.parametrize(
    "spo2,on_oxygen,expected",
    [
        (83, False, 3),
        (84, False, 2),
        (85, False, 2),
        (86, False, 1),
        (87, False, 1),
        (88, False, 0),
        (92, False, 0),
        (96, False, 0),  # on air anything >= 88 scores 0
        (92, True, 0),
        (93, True, 1),
        (94, True, 1),
        (95, True, 2),
        (96, True, 2),
        (97, True, 3),
    ],
)
def test_spo2_scale_2(spo2, on_oxygen, expected):
    assert news2.score_spo2_scale2(spo2, on_oxygen) == expected


@pytest.mark.parametrize("sbp,expected", [(90, 3), (91, 2), (100, 2), (101, 1), (110, 1), (111, 0), (219, 0), (220, 3)])
def test_systolic(sbp, expected):
    assert news2.score_systolic(sbp) == expected


@pytest.mark.parametrize(
    "hr,expected", [(40, 3), (41, 1), (50, 1), (51, 0), (90, 0), (91, 1), (110, 1), (111, 2), (130, 2), (131, 3)]
)
def test_pulse(hr, expected):
    assert news2.score_pulse(hr) == expected


@pytest.mark.parametrize(
    "temp,expected", [(35.0, 3), (35.1, 1), (36.0, 1), (36.1, 0), (38.0, 0), (38.1, 1), (39.0, 1), (39.1, 2)]
)
def test_temperature(temp, expected):
    assert news2.score_temperature(temp) == expected


def test_all_normal_scores_zero_and_low():
    r = news2.compute(Observation(respiratory_rate=16, spo2=97, systolic=125, pulse=72, temperature=36.8))
    assert r.score == 0
    assert r.band == Band.LOW
    assert r.complete


def test_single_red_parameter_is_low_medium():
    # RR 7 alone scores 3; total 3 is "low" by aggregate but a single 3 triggers an urgent review
    r = news2.compute(Observation(respiratory_rate=7, spo2=97, systolic=125, pulse=72, temperature=36.8))
    assert r.score == 3
    assert r.band == Band.LOW_MEDIUM


def test_medium_band():
    # RR 22 (2) + SpO2 95 (1) + HR 105 (1) + temp 38.4 (1) = 5
    r = news2.compute(Observation(respiratory_rate=22, spo2=95, systolic=120, pulse=105, temperature=38.4))
    assert r.score == 5
    assert r.band == Band.MEDIUM


def test_septic_picture_is_high():
    # RR 26 (3) + SpO2 92 (2) + SBP 98 (2) + HR 124 (2) + temp 39.2 (2) = 11
    r = news2.compute(Observation(respiratory_rate=26, spo2=92, systolic=98, pulse=124, temperature=39.2))
    assert r.score == 11
    assert r.band == Band.HIGH
    assert r.components == {
        "RespiratoryRate": 3,
        "SpO2": 2,
        "AirOrOxygen": 0,
        "Systolic": 2,
        "Pulse": 2,
        "Consciousness": 0,
        "Temperature": 2,
    }


def test_supplemental_oxygen_adds_two():
    air = news2.compute(Observation(respiratory_rate=16, spo2=97, systolic=125, pulse=72, temperature=36.8))
    o2 = news2.compute(
        Observation(respiratory_rate=16, spo2=97, on_oxygen=True, systolic=125, pulse=72, temperature=36.8)
    )
    assert o2.score == air.score + 2


def test_copd_patient_on_scale_2_is_not_penalised_for_target_saturation():
    obs = Observation(respiratory_rate=18, spo2=89, systolic=130, pulse=80, temperature=36.9)
    assert news2.compute(obs, spo2_scale=1).components["SpO2"] == 3
    assert news2.compute(obs, spo2_scale=2).components["SpO2"] == 0


def test_new_confusion_scores_three():
    r = news2.compute(
        Observation(respiratory_rate=16, spo2=97, systolic=125, pulse=72, temperature=36.8, consciousness_alert=False)
    )
    assert r.components["Consciousness"] == 3
    assert r.band == Band.LOW_MEDIUM


def test_missing_parameters_marks_incomplete():
    r = news2.compute(Observation(pulse=72, spo2=97))
    assert not r.complete
    assert "RespiratoryRate" not in r.components
