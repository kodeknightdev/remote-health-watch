from datetime import UTC, date, datetime

from app import kpis


def test_alerts_per_day_fills_empty_days():
    raised = [datetime(2025, 5, 20, 9, tzinfo=UTC), datetime(2025, 5, 20, 17, tzinfo=UTC), datetime(2025, 5, 22, 1, tzinfo=UTC)]
    result = kpis.alerts_per_day(raised, date(2025, 5, 19), date(2025, 5, 22))
    assert result == [(date(2025, 5, 19), 0), (date(2025, 5, 20), 2), (date(2025, 5, 21), 0), (date(2025, 5, 22), 1)]


def test_average_minutes_to_ack():
    assert kpis.average_minutes_to_ack([60, 180]) == 2.0
    assert kpis.average_minutes_to_ack([]) is None


def test_escalation_rate_counts_alerts_not_escalation_steps():
    assert kpis.escalation_rate([0, 2, 1, 0]) == 0.5
    assert kpis.escalation_rate([]) == 0.0


def test_wear_fraction_is_capped_and_partial():
    # one reading a minute expected
    assert kpis.wear_fraction(60, 60) == 1.0
    assert kpis.wear_fraction(30, 60) == 0.5
    assert kpis.wear_fraction(500, 60) == 1.0


def test_wear_compliance_over_expected_hours():
    hours = [(60, 60)] * 18 + [(30, 60)] * 4
    assert kpis.wear_compliance(hours, 24) == round((18 + 2) / 24, 3)
    assert kpis.wear_compliance([], 0) is None
