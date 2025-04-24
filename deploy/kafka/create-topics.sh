#!/bin/bash
# Creates every topic with its .retry and .dlq lanes. Safe to re-run.
set -euo pipefail

BOOTSTRAP="${KAFKA_BOOTSTRAP:-kafka:9092}"
KAFKA_BIN="${KAFKA_BIN:-/opt/kafka/bin}"
TOPICS_FILE="${TOPICS_FILE:-/scripts/topics.txt}"
RF="${REPLICATION_FACTOR:-1}"

until "$KAFKA_BIN/kafka-topics.sh" --bootstrap-server "$BOOTSTRAP" --list >/dev/null 2>&1; do
  echo "waiting for kafka at $BOOTSTRAP..."
  sleep 2
done

create() {
  local name=$1 partitions=$2 retention=$3
  "$KAFKA_BIN/kafka-topics.sh" --bootstrap-server "$BOOTSTRAP" --create --if-not-exists \
    --topic "$name" --partitions "$partitions" --replication-factor "$RF" \
    --config retention.ms="$retention" >/dev/null
  echo "  $name ($partitions)"
}

echo "creating topics on $BOOTSTRAP"
grep -v '^\s*#' "$TOPICS_FILE" | grep -v '^\s*$' | while read -r topic partitions retention; do
  create "$topic" "$partitions" "$retention"
  # retry lane keeps the partition count so a patient's retries stay ordered among themselves
  create "$topic.retry" "$partitions" 604800000
  create "$topic.dlq" 1 -1
done
create healthchecks 1 3600000
echo "done"
