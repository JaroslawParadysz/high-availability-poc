# ADR 0003: Idempotency and Duplicate Tracking Strategy

## Status
Accepted (concept phase)

## Date
2026-05-13

## Context
The connector delivers messages at-least-once. RabbitMQ may redeliver a message after a NACK or a consumer reconnect. The persistence layer must handle duplicates safely without relying on application-level pre-checks that introduce race conditions.

Additionally, duplicate arrivals should be visible for operational monitoring — a high duplicate rate may indicate upstream issues or misconfigured retry topology.

## Decision Drivers
- Idempotency must be enforced at the DB constraint level, not in application code.
- No read-before-write (check-then-insert) patterns; these are racy under concurrent delivery.
- Duplicate events must be observable; silent discard is not acceptable.
- Schema must remain queryable and auditable.

## Options Considered

### Idempotency Key
| Option | Notes |
|--------|-------|
| `CorrelationId` from RabbitMQ `BasicProperties` | Already present in message metadata; stable across redeliveries; chosen |
| Generated UUID at consume time | Not stable on redelivery; would create a duplicate row on retry; rejected |

### Missing Correlation ID Handling
| Option | Notes |
|--------|-------|
| Generate random GUID as fallback | Breaks deduplication on retry; rejected |
| NACK → DLQ immediately | Safe and explicit; chosen |

### Duplicate Record Strategy
| Option | Notes |
|--------|-------|
| `ON CONFLICT DO NOTHING` (silent skip) | Simple; no visibility into duplicate rate; rejected |
| `ON CONFLICT DO UPDATE SET duplicate_count++` | Single table, counter only; insufficient for arrival-level audit; rejected |
| `ON CONFLICT → insert into duplicate_events` *(chosen)* | Full arrival history; unique constraint intact on `communication_log`; alertable per duplicate event |

### Status Values
| Option | Notes |
|--------|-------|
| `'pending'`, `'processed'`, `'failed'` | `'pending'` status requires a second update; if that update fails the row is stuck as pending with no recovery path; rejected |
| `'processed'`, `'failed'` only *(chosen)* | Single-insert pattern; no stuck rows |

### handled_at Column Naming
| Option | Notes |
|--------|-------|
| `processed_at` (set only on success) | Misleading for failed records; null on failure reduces query utility; rejected |
| `handled_at` (set always) *(chosen)* | Unambiguous: records the moment the connector attempted to handle the message |

## Decision
- **Idempotency key**: `CorrelationId` from RabbitMQ `BasicProperties`; enforced via `UNIQUE` constraint on `communication_log.correlation_id`
- **Missing correlation ID**: NACK with `requeue: false` → DLQ immediately; log and metric the event
- **Duplicate handling**: `ON CONFLICT (correlation_id)` → insert a row into `duplicate_events` within the same DB transaction
- **Status values**: `'processed'` and `'failed'` only; no `'pending'`
- **Timestamp column**: `handled_at NOT NULL`; set at insert time for every record regardless of outcome

## Schema

```sql
CREATE TABLE communication_log (
    id               SERIAL PRIMARY KEY,
    correlation_id   UUID UNIQUE NOT NULL,
    message_body     TEXT NOT NULL,
    received_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    handled_at       TIMESTAMP NOT NULL,
    status           VARCHAR(50) NOT NULL,  -- 'processed', 'failed'
    error_message    TEXT,
    source_queue     VARCHAR(255)
);

CREATE TABLE duplicate_events (
    id               SERIAL PRIMARY KEY,
    correlation_id   UUID NOT NULL,
    received_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    source_queue     VARCHAR(255)
);
```

## Rationale
- DB-level `UNIQUE` constraint is the only race-free idempotency mechanism; application-level checks introduce a TOCTOU window.
- `duplicate_events` table gives operators a queryable arrival log; high duplicate rates can trigger alerts without scanning `communication_log`.
- Both inserts in the same transaction ensure no partial state: either the message is recorded as handled, or the duplicate is recorded, never neither and never both.
- `'pending'` status was dropped because a two-phase insert-then-update pattern creates stuck rows when the update step fails and redelivery hits the idempotency constraint on the second attempt.

## Consequences

### Positive
- Idempotency is guaranteed at the storage level regardless of application-level bugs.
- Duplicate arrival history is fully auditable and independently queryable.
- No read-before-write; single round-trip to the DB per message.

### Negative
- Two tables to maintain and migrate.
- `duplicate_events` adds a second write per duplicate; acceptable given duplicate rate should be low in normal operation.
- `duplicate_events.correlation_id` has no foreign key to `communication_log` (a duplicate could theoretically arrive before the original is committed in an edge case); this is acceptable for the audit use case.

### Neutral / Constraints
- `source_queue` is populated from `IOptions<RabbitMqOptions>` in the handler; no interface change to `IMessageHandler` is required.
- Message body size, PII handling, and encryption-at-rest are out of scope for this ADR.

## Follow-up Actions
1. Implement upsert logic in `CommunicationLogHandler`: insert into `communication_log`, on conflict insert into `duplicate_events`, both in one transaction.
2. Add missing correlation ID guard in `RabbitMqConsumerWorker`: NACK `requeue: false` before dispatching to handler.
3. Add structured log entry and metric counter for rejected (missing correlation ID) messages.
4. Add structured log entry and metric counter for duplicate events.

## References
- `docs/research/database-persistence-concept.md`
- ADR 0002: Persistence Store Selection
