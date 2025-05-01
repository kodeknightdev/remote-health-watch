import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker

from app.config import Settings
from app.models import Base


@pytest.fixture
def settings() -> Settings:
    return Settings(database_url="sqlite://", kafka_enabled=False, jwt_signing_key="x" * 40)


@pytest.fixture
def session():
    engine = create_engine("sqlite://")
    Base.metadata.create_all(engine)
    s = sessionmaker(bind=engine, expire_on_commit=False)()
    yield s
    s.close()
