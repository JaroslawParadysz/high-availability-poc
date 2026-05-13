# Database Persistence Concept

**Status**: Concept Phase  
**Date**: May 2026  
**Target Availability**: 99.99%

## Problem Statement

The current connector consumes messages from RabbitMQ but does **not persist a record** of what was processed. This creates:

- No audit trail for compliance/troubleshooting
- No ability to replay or investigate failed deliveries
- No visibility into message processing history
- Risk of data loss if a downstream step fails after consume but before DB write is confirmed

## Goals

1. **Durability**: Persist every consumed RabbitMQ message to a relational database (PostgreSQL)
2. **Idempotency**: Guarantee that duplicate messages do not create duplicate DB records
3. **Correlation**: Track messages end-to-end via correlation IDs persisted in the DB record
4. **Observability**: Enable querying and auditing of message flow
5. **Resilience**: Connector process remains alive during DB outages; processing resumes automatically when DB recovers without manual intervention. Full 99.99% pipeline availability requires DB HA (covered in infrastructure phase).

## Scope

### In Scope
- Create `CommunicationLog` table to store consumed messages
- Replace placeholder `DefaultMessageHandler` with EF Core persistence logic
- Add PostgreSQL container to docker-compose
- Implement idempotent message deduplication (correlation ID as unique key)
- Add health check for database connectivity
- Unit and integration tests for persistence layer
- Configuration management (connection strings, retry policy)

### Out of Scope (Future)
- Downstream delivery status tracking (separate feature)
- Outbox pattern for transactional consistency (Phase 2)
- Message replay/reprocessing CLI (Phase 3)
- Database replication/high-availability setup (Azure infrastructure phase)

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| ORM | Entity Framework Core 10 | .NET standard, Npgsql driver mature, migrations first-class |
| Database | PostgreSQL 15+ | Open-source, JSONB for flexible message storage, proven HA story |
| Idempotency Key | `Message.CorrelationId` | Always present; messages missing a correlation ID are rejected (NACK → DLQ) |
| Missing Correlation ID | NACK → DLQ immediately | Silently generating a random ID would break deduplication on retry |
| DbContext Lifetime | Scope-per-message via `IServiceScopeFactory` | Worker is singleton; EF Core DbContext must be scoped to avoid state corruption across messages |
| Retry Strategy | Requeue-based via `TransientPersistenceException` + `x-delivery-count` header | Keeps consumer free during DB outages; RabbitMQ increments `x-delivery-count` automatically on requeue; cap retries to prevent infinite loops; move to DLX delayed retry for backoff in production |
| Transient vs Permanent Errors | Npgsql transient errors → `TransientPersistenceException` → NACK requeue; all others → NACK DLQ | Avoids dead-lettering recoverable failures without hiding permanent ones |
| Record Status Values | `'processed'` and `'failed'` only | Single-insert pattern; `'pending'` dropped — no current use case and causes stuck rows if update fails |
| DB Migrations | `MigrateAsync()` at app startup | Single instance for now; move to dedicated migration job before multi-instance production deployment to avoid startup races |
| `source_queue` Population | Injected from `IOptions<RabbitMqOptions>` in handler | Single queue per instance; no need to thread queue name through `IMessageHandler` interface |

## Architecture

```
┌─────────────────┐
│   RabbitMQ      │
└────────┬────────┘
         │ BasicDeliverEventArgs
         ▼
┌─────────────────────────────┐
│  RabbitMqConsumerWorker     │
│  (Workers folder)           │
└────────┬────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│ IMessageHandler             │
│ (Abstractions)              │
└────────┬────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│ CommunicationLogHandler     │
│ (Messaging/Handlers)        │
├─────────────────────────────┤
│ 1. Validate message         │
│ 2. Upsert to CommunicationLog│
│    (ON CONFLICT → dup_events)│
│ 3. Acknowledge RabbitMQ msg │
└─────────────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│ PostgreSQL (docker)         │
│ Table: CommunicationLog     │
└─────────────────────────────┘
```

## CommunicationLog Table Schema

```sql
CREATE TABLE communication_log (
    id SERIAL PRIMARY KEY,
    correlation_id UUID UNIQUE NOT NULL,  -- Idempotency key; enforced by DB constraint
    message_body TEXT NOT NULL,
    received_at TIMESTAMP NOT NULL DEFAULT NOW(),
    handled_at TIMESTAMP NOT NULL,  -- Set at insert time regardless of outcome
    status VARCHAR(50) NOT NULL,  -- 'processed', 'failed'
    error_message TEXT,
    source_queue VARCHAR(255),
    INDEX idx_correlation_id (correlation_id),
    INDEX idx_received_at (received_at),
    INDEX idx_status (status)
);

CREATE TABLE duplicate_events (
    id SERIAL PRIMARY KEY,
    correlation_id UUID NOT NULL,  -- References communication_log.correlation_id
    received_at TIMESTAMP NOT NULL DEFAULT NOW(),
    source_queue VARCHAR(255),
    INDEX idx_dup_correlation_id (correlation_id),
    INDEX idx_dup_received_at (received_at)
);
```

Duplicate handling: attempt insert into `communication_log`; on `ON CONFLICT (correlation_id)` insert a row into `duplicate_events` instead. Both operations within the same DB transaction.

## Failure Scenarios & Mitigations

| Scenario | Current Behavior | Mitigation |
|----------|------------------|-----------|
| DB connection fails during write (transient) | Message requeued | Throw `TransientPersistenceException`; worker NACKs with `requeue: true`; check `x-delivery-count` header; NACK to DLQ after N retries |
| DB connection fails during write (permanent) | Message dead-lettered | Worker catches non-transient exception; NACKs with `requeue: false` → DLQ |
| Duplicate message on replay | DB constraint violation | `ON CONFLICT (correlation_id)` → insert row into `duplicate_events` within same transaction |
| Message missing correlation ID | Previously: random GUID generated (breaks dedup) | NACK with `requeue: false` → DLQ immediately; alert on DLQ growth |
| DB write latency > 5s | Consumer thread blocks | Async EF Core with configurable `CommandTimeout`; treated as transient failure if timeout |
| PostgreSQL container crashes | Consumer stops | Health check reports unhealthy; operator alerted; messages requeue until DB recovers |

## Success Criteria

1. ✅ All consumed messages appear in `communication_log` table within 100ms
2. ✅ Duplicate messages (same correlation ID) do not create duplicate rows
3. ✅ DB health check reports unhealthy when PostgreSQL unavailable
4. ✅ All 19 existing tests pass + 8 new integration tests pass
5. ✅ docker-compose up -d brings both RabbitMQ and PostgreSQL with health checks
6. ✅ Connection string is externalized (no hardcoding)
7. ✅ Async/await throughout; no thread blocking

## Open Questions

1. **Data retention**: How long to keep logs? → Future: retention policy / archive design
2. **Encryption**: Should correlation ID or message body be encrypted at rest? → Future: security review
3. **Message body size & sensitivity**: No size cap or PII handling currently. Revisit before production: consider max length config, truncation flag, and encryption-at-rest policy.

## Next Steps

1. **ADRs**: Create decision records for EF/Postgres, schema design, and outbox pattern
2. **Implementation tasks**: Break down into 7 tracked GitHub issues
3. **First commit**: docker-compose + schema + DbContext setup
4. **Integration**: Replace DefaultMessageHandler with CommunicationLogHandler
