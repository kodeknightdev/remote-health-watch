"""Kafka plumbing shared by the consumers: retry and dead-letter lanes, idempotency, and an outbox relay."""

import logging
import threading
import time
import uuid
from collections.abc import Callable
from datetime import datetime, timedelta

from confluent_kafka import Consumer, KafkaError, KafkaException, Producer
from opentelemetry import trace
from sqlalchemy import select, update
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app import db, telemetry
from app.events import Envelope, Outgoing, Topics
from app.models import OutboxEvent, ProcessedEvent

log = logging.getLogger(__name__)
tracer = trace.get_tracer("remote-health-watch.messaging")

Handler = Callable[[Session, Envelope, str], list[Outgoing]]


def stage(s: Session, events: list[Outgoing], source: str) -> None:
    """Writes outgoing events to the outbox inside the caller's transaction."""
    traceparent = telemetry.current_traceparent()
    for e in events:
        event_id, payload = e.envelope(source)
        s.add(OutboxEvent(id=event_id, topic=e.topic, key=e.key, payload=payload, traceparent=traceparent,
                          created_at=db.naive(db.utcnow()), attempts=0))


def run_once(s: Session, event_id: str, consumer: str, work: Callable[[], None]) -> bool:
    if s.get(ProcessedEvent, (event_id, consumer)) is not None:
        return False
    work()
    s.add(ProcessedEvent(event_id=event_id, consumer=consumer, processed_at=db.naive(db.utcnow())))
    return True


class ConsumerLoop(threading.Thread):
    def __init__(self, *, bootstrap: str, group: str, topics: list[str], handler: Handler, source: str,
                 producer: Producer, retry_lane: bool, max_attempts: int, base_delay: float):
        super().__init__(daemon=True, name=f"{group}{'-retry' if retry_lane else ''}")
        self.group = group + ("-retry" if retry_lane else "")
        self.topics = [t + Topics.RETRY_SUFFIX for t in topics] if retry_lane else topics
        self.handler = handler
        self.source = source
        self.producer = producer
        self.retry_lane = retry_lane
        self.max_attempts = max_attempts
        self.base_delay = base_delay
        self.bootstrap = bootstrap
        self._halt = threading.Event()
        self.consumer_name = group

    def stop(self) -> None:
        self._halt.set()

    def run(self) -> None:
        consumer = Consumer({
            "bootstrap.servers": self.bootstrap,
            "group.id": self.group,
            "auto.offset.reset": "earliest",
            "enable.auto.commit": True,
            "enable.auto.offset.store": False,
            "partition.assignment.strategy": "cooperative-sticky",
        })
        consumer.subscribe(self.topics)
        log.info("consumer %s listening on %s", self.group, ",".join(self.topics))
        try:
            while not self._halt.is_set():
                msg = consumer.poll(0.5)
                if msg is None:
                    continue
                if msg.error():
                    if msg.error().code() not in (KafkaError._PARTITION_EOF, KafkaError.UNKNOWN_TOPIC_OR_PART):
                        log.warning("consume error on %s: %s", self.group, msg.error())
                    continue
                self._process(msg)
                consumer.store_offsets(msg)
        finally:
            consumer.close()

    def _process(self, msg) -> None:
        headers = {k: v.decode() for k, v in (msg.headers() or [])}
        base_topic = headers.get("x-original-topic", msg.topic())
        attempt = int(headers.get("x-attempt", "0"))
        if self.retry_lane and "x-not-before" in headers:
            wait = (datetime.fromisoformat(headers["x-not-before"]) - db.utcnow()).total_seconds()
            if wait > 0:
                time.sleep(min(wait, 60))

        ctx = telemetry.extract_context(msg.headers())
        with tracer.start_as_current_span(f"{base_topic} process", context=ctx, kind=trace.SpanKind.CONSUMER) as span:
            span.set_attribute("messaging.system", "kafka")
            span.set_attribute("messaging.destination.name", base_topic)
            try:
                envelope = Envelope.parse(msg.value())
            except Exception as ex:
                log.error("unreadable message on %s offset %s: %s", msg.topic(), msg.offset(), ex)
                self._forward(base_topic + Topics.DLQ_SUFFIX, msg, headers, attempt, str(ex))
                return
            try:
                with db.session() as s:
                    def work():
                        stage(s, self.handler(s, envelope, base_topic), self.source)
                    run_once(s, envelope.id, self.consumer_name, work)
            except IntegrityError:
                # another replica committed the same event first
                log.debug("duplicate %s for %s", envelope.id, self.consumer_name)
            except Exception as ex:
                span.record_exception(ex)
                nxt = attempt + 1
                target = base_topic + (Topics.DLQ_SUFFIX if nxt >= self.max_attempts else Topics.RETRY_SUFFIX)
                log.warning("handler %s failed for %s %s (attempt %s), routing to %s: %s",
                            self.consumer_name, envelope.type, envelope.id, nxt, target, ex)
                self._forward(target, msg, headers, nxt, str(ex))

    def _forward(self, topic: str, msg, headers: dict[str, str], attempt: int, error: str) -> None:
        headers = dict(headers)
        headers["x-original-topic"] = headers.get("x-original-topic", msg.topic())
        headers["x-attempt"] = str(attempt)
        headers["x-error"] = error[:500]
        delay = self.base_delay * (2 ** max(0, attempt - 1))
        headers["x-not-before"] = (db.utcnow() + timedelta(seconds=delay)).isoformat()
        self.producer.produce(topic, key=msg.key(), value=msg.value(), headers=list(headers.items()))
        self.producer.flush(10)


class OutboxRelay(threading.Thread):
    def __init__(self, producer: Producer, poll_seconds: float = 0.25, batch: int = 200):
        super().__init__(daemon=True, name="outbox-relay")
        self.producer = producer
        self.poll_seconds = poll_seconds
        self.batch = batch
        self._halt = threading.Event()

    def stop(self) -> None:
        self._halt.set()

    def run(self) -> None:
        while not self._halt.is_set():
            try:
                sent = self.relay_once()
            except Exception as ex:
                log.warning("outbox relay failed: %s", ex)
                sent = 0
                time.sleep(2)
            if sent < self.batch:
                time.sleep(self.poll_seconds)

    def relay_once(self) -> int:
        with db.session() as s:
            rows = s.execute(
                select(OutboxEvent).where(OutboxEvent.published_at.is_(None), OutboxEvent.attempts < 20)
                .order_by(OutboxEvent.created_at).limit(self.batch)
            ).scalars().all()
            if not rows:
                return 0
            failures: dict[str, str] = {}

            def on_delivery(row_id):
                def cb(err, _msg):
                    if err is not None:
                        failures[row_id] = str(err)
                return cb

            for row in rows:
                headers = [("event-id", row.id)]
                if row.traceparent:
                    headers.append(("traceparent", row.traceparent))
                self.producer.produce(row.topic, key=row.key, value=row.payload, headers=headers,
                                      on_delivery=on_delivery(row.id))
            self.producer.flush(15)
            now = db.naive(db.utcnow())
            ok = [r.id for r in rows if r.id not in failures]
            if ok:
                s.execute(update(OutboxEvent).where(OutboxEvent.id.in_(ok)).values(published_at=now))
            for row_id, err in failures.items():
                log.warning("outbox %s failed: %s", row_id, err)
                s.execute(update(OutboxEvent).where(OutboxEvent.id == row_id).values(attempts=OutboxEvent.attempts + 1))
            return len(rows)


def make_producer(bootstrap: str, client_id: str) -> Producer:
    return Producer({
        "bootstrap.servers": bootstrap,
        "client.id": client_id,
        "enable.idempotence": True,
        "acks": "all",
        "linger.ms": 5,
        "compression.type": "lz4",
    })


def new_id() -> str:
    return str(uuid.uuid4())


__all__ = ["ConsumerLoop", "OutboxRelay", "make_producer", "stage", "run_once", "KafkaException"]
