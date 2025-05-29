from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="INSIGHTS_", env_file=".env", extra="ignore")

    service_name: str = "insights"
    database_url: str = "sqlite:///./insights.db"
    kafka_bootstrap: str = "localhost:9092"
    kafka_enabled: bool = True
    consumer_group: str = "insights"
    max_delivery_attempts: int = 4
    retry_base_delay_seconds: float = 2.0

    jwt_signing_key: str = ""
    jwt_issuer: str = "remote-health-watch-identity"
    jwt_audience: str = "remote-health-watch"
    patients_url: str = "http://localhost:5102"

    blob_connection_string: str = "UseDevelopmentStorage=true"
    blob_container: str = "reports"
    # weekly reports run Monday morning for the previous Monday-Sunday
    weekly_report_weekday: int = 0
    weekly_report_hour_utc: int = 6


@lru_cache
def get_settings() -> Settings:
    return Settings()
