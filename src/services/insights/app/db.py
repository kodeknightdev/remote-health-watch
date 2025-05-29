from collections.abc import Iterator
from contextlib import contextmanager
from datetime import UTC, datetime

from sqlalchemy import create_engine
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session, sessionmaker

_engine: Engine | None = None
_factory: sessionmaker[Session] | None = None


def init(url: str) -> Engine:
    global _engine, _factory
    kwargs = {"pool_pre_ping": True}
    if url.startswith("mssql"):
        kwargs |= {"pool_size": 10, "max_overflow": 10, "fast_executemany": True}
    _engine = create_engine(url, **kwargs)
    _factory = sessionmaker(bind=_engine, expire_on_commit=False)
    return _engine


def engine() -> Engine:
    assert _engine is not None, "db.init() not called"
    return _engine


@contextmanager
def session() -> Iterator[Session]:
    assert _factory is not None, "db.init() not called"
    s = _factory()
    try:
        yield s
        s.commit()
    except Exception:
        s.rollback()
        raise
    finally:
        s.close()


def utcnow() -> datetime:
    return datetime.now(UTC)


def naive(dt: datetime) -> datetime:
    """SQL Server datetime2 has no offset; everything is stored as naive UTC."""
    return dt.astimezone(UTC).replace(tzinfo=None) if dt.tzinfo else dt


def aware(dt: datetime) -> datetime:
    return dt.replace(tzinfo=UTC) if dt.tzinfo is None else dt
