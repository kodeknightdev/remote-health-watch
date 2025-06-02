from datetime import UTC, datetime

from app.main import next_weekly_run


def test_next_run_is_the_coming_monday_morning():
    wed = datetime(2025, 5, 21, 15, tzinfo=UTC)
    assert next_weekly_run(wed, 0, 6) == datetime(2025, 5, 26, 6, tzinfo=UTC)


def test_monday_after_the_hour_rolls_to_next_week():
    mon = datetime(2025, 5, 26, 7, tzinfo=UTC)
    assert next_weekly_run(mon, 0, 6) == datetime(2025, 6, 2, 6, tzinfo=UTC)
