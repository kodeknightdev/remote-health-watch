import json
import logging
import re
import sys

SENSITIVE_KEYS = {"firstname", "lastname", "name", "dateofbirth", "dob", "mrn", "address", "phone", "email", "password"}
_DOB = re.compile(r"\b(19|20)\d{2}-\d{2}-\d{2}\b(?!T)")
_MRN = re.compile(r"\bMRN-\d{6,}\b", re.IGNORECASE)


def redact(value):
    """Masks PHI-looking fields and strings. Surrogate GUIDs pass through."""
    if isinstance(value, dict):
        return {k: "[REDACTED]" if k.lower() in SENSITIVE_KEYS else redact(v) for k, v in value.items()}
    if isinstance(value, list):
        return [redact(v) for v in value]
    if isinstance(value, str):
        return _MRN.sub("[REDACTED]", _DOB.sub("[REDACTED]", value))
    return value


class JsonFormatter(logging.Formatter):
    def __init__(self, service: str):
        super().__init__()
        self.service = service

    def format(self, record: logging.LogRecord) -> str:
        body = {
            "ts": self.formatTime(record, "%Y-%m-%dT%H:%M:%S"),
            "level": record.levelname,
            "service": self.service,
            "logger": record.name,
            "msg": redact(record.getMessage()),
        }
        extra = getattr(record, "fields", None)
        if extra:
            body["fields"] = redact(extra)
        if record.exc_info:
            body["exc"] = self.formatException(record.exc_info)
        return json.dumps(body)


def configure(service: str, level: str = "INFO") -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter(service))
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(level)
    for noisy in ("uvicorn.access", "httpx", "azure"):
        logging.getLogger(noisy).setLevel(logging.WARNING)
