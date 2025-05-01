from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="RISK_", env_file=".env", extra="ignore")

    service_name: str = "risk-engine"
    database_url: str = "sqlite:///./risk.db"
    kafka_bootstrap: str = "localhost:9092"
    kafka_enabled: bool = True
    consumer_group: str = "risk-engine"
    max_delivery_attempts: int = 4
    retry_base_delay_seconds: float = 2.0

    jwt_signing_key: str = ""
    jwt_issuer: str = "remote-health-watch-identity"
    jwt_audience: str = "remote-health-watch"
    patients_url: str = "http://localhost:5102"

    news2_window_minutes: int = 60
    # publish an unchanged score at least this often so the dashboard never shows a stale band
    score_heartbeat_minutes: int = 5
    baseline_hours: int = 24
    baseline_min_samples: int = 30
    zscore_threshold: float = 3.5
    anomaly_cooldown_minutes: int = 15
    iforest_min_hours: float = 24
    iforest_retrain_minutes: int = 60
    trend_window_hours: float = 6
    trend_cooldown_minutes: int = 120
    observation_retention_hours: int = 36


@lru_cache
def get_settings() -> Settings:
    return Settings()
