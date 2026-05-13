# ADR 0004: Retry and Failure Classification Strategy for DB Writes

## Status
Accepted (concept phase)

## Date
2026-05-13

## Context
The connector must distinguish transient DB failures (connection timeout, brief outage) from permanent failures (constraint violations, misconfiguration) and respond differently: transient failures should be retried, permanent failures should be dead-lettered without retry.

Without explicit classification, all DB failures would result in the same NACK behavior, either silently discarding recoverable errors or endlessly retrying unrecoverable ones.

## Decision Drivers
- Transient DB outages must not result in message loss.
- Permanent errors must not cause infinite retry loops.
- Retry mechanism must not block the consumer thread while waiting.
- Minimal new dependencies for the PoC phase.
- Retry count must be capped and observable.

## Options Considered

### Retry Mechanism
| Option | Notes |
|--------|-------|
| Polly retry policy inside the handler | Holds message unacknowledged during retries; blocks consumer under sustained DB outage; increases latency for all messages; rejected for this phase |
| DLX + TTL queue (delayed retry topology) | Best for exponential backoff and thundering-herd prevention; requires RabbitMQ topology changes; deferred to production |
| Requeue via NACK + `x-delivery-count` *(chosen for PoC)* | No topology changes; consumer is freed immediately; RabbitMQ manages retry timing; count tracked via built-in header |

### Retry Count Tracking
| Option | Notes |
|--------|-------|
| Custom `x-retry-count` header | Must be read, incremented, and re-published manually; NACK requeue does not update headers; complex and fragile; rejected |
| `x-delivery-count` built-in header *(chosen)* | RabbitMQ increments automatically on every requeue; no re-publish logic required |
| `x-death` header (DLX trips) | Tracks DLQ routing events only, not simple requeues; not applicable here |

### Transient Error Classification
| Option | Notes |
|--------|-------|
| Catch all exceptions and requeue | Hides permanent errors; rejected |
| Npgsql `IsTransient` property on `NpgsqlException` *(chosen)* | Npgsql marks retriable errors (connection refused, timeout, transient server errors) via `IsTransient`; wrap in `TransientPersistenceException` |
| Catch specific Npgsql error codes | More precise but fragile to maintain; `IsTransient` is the recommended Npgsql pattern |

## Decision
- **Transient DB errors**: `CommunicationLogHandler` catches `NpgsqlException` where `IsTransient == true` and throws `TransientPersistenceException`.
- **Worker response to transient error**: Check `x-delivery-count` header. If below configurable `MaxRetryCount`, NACK with `requeue: true`. If at or above cap, NACK with `requeue: false` → DLQ.
- **Worker response to all other exceptions**: NACK with `requeue: false` → DLQ immediately.
- **`MaxRetryCount`**: Externalized in configuration; no hardcoded value.
- **Future**: Replace with DLX + TTL queue for exponential backoff when moving to production.

## Retry Flow

```
Message received
      │
      ▼
CommunicationLogHandler.HandleAsync()
      │
      ├─ NpgsqlException (IsTransient=true)
      │         │
      │         └─ throw TransientPersistenceException
      │                   │
      │                   ▼
      │         Worker catches TransientPersistenceException
      │                   │
      │         x-delivery-count < MaxRetryCount?
      │                   │
      │            Yes ───┴─── No
      │             │           │
      │      NACK requeue    NACK → DLQ
      │
      ├─ Any other exception
      │         │
      │         └─ NACK → DLQ immediately
      │
      └─ Success
                │
                └─ ACK
```

## Rationale
- Requeue-based retry keeps the consumer free during DB outages; Polly retry inside the handler would hold the unacknowledged message and block the consumer thread for the full retry duration.
- `x-delivery-count` is the simplest correct solution: RabbitMQ increments it on every requeue, requiring no custom header management or re-publish logic.
- `NpgsqlException.IsTransient` is the idiomatic Npgsql classification; it correctly identifies connection failures, command timeouts, and server-side transient conditions.
- DLX delayed retry is the right long-term approach for backoff and thundering-herd prevention; it is deferred because it requires RabbitMQ topology changes outside the connector's scope for the PoC phase.

## Consequences

### Positive
- Consumer thread is immediately freed on transient failure; no blocking retry loops.
- Retry count is observable via `x-delivery-count` (visible in RabbitMQ management UI and message headers).
- Permanent errors go to DLQ on first failure; no wasted retries.

### Negative
- Immediate requeue on transient failure may cause a high requeue rate during extended DB outages (no backoff). Mitigated by the retry cap; messages eventually DLQ rather than loop forever.
- `x-delivery-count` is reset if the message is republished (e.g., moved from DLQ back to the main queue manually); operators must be aware of this.

### Neutral / Constraints
- DLQ must be configured on the RabbitMQ queue for `requeue: false` NACKs to route correctly. This is a RabbitMQ infrastructure concern, not covered by this ADR.
- `MaxRetryCount` default value and alert threshold are implementation decisions, not captured here.

## Follow-up Actions
1. Create `TransientPersistenceException` in the connector.
2. Update `RabbitMqConsumerWorker.OnMessageReceivedAsync` to catch `TransientPersistenceException`, read `x-delivery-count`, and branch to requeue or DLQ.
3. Externalize `MaxRetryCount` in `appsettings.json` and bind via options pattern.
4. Add structured log entry on retry (include `x-delivery-count` and `correlation_id`).
5. Revisit: replace with DLX + TTL queue before production deployment.

## References
- `docs/research/database-persistence-concept.md`
- ADR 0002: Persistence Store Selection
- ADR 0003: Idempotency and Duplicate Tracking
- Npgsql transient error handling: https://www.npgsql.org/doc/failover-and-load-balancing.html
