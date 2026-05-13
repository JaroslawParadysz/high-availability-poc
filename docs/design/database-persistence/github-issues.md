# GitHub Issues — Database Persistence Feature
_Generated: 2026-05-13_
_Repo: JaroslawParadysz/high-availability-poc_
_ADRs: 0002, 0003, 0004_

---

## Issue 1 — [Infra] Add PostgreSQL to docker-compose
**Labels**: `infrastructure`, `database`

Add a PostgreSQL 15+ container to `docker-compose.yml` with:
- A named volume for data persistence
- A health check (`pg_isready`)
- Environment variables for `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
- Connection string exposed as an env var for the connector service
- Connector service `depends_on` PostgreSQL with `condition: service_healthy`

**Acceptance criteria**:
- `docker-compose up -d` starts both RabbitMQ and PostgreSQL
- PostgreSQL health check passes before the connector starts
- Connection string is not hardcoded

---

## Issue 2 — [DB] Create EF Core DbContext, entities, and initial migration
**Labels**: `database`, `ef-core`

Create `ConnectorDbContext` with two entity types:
- `CommunicationLog` — maps to `communication_log` table (schema per ADR 0003)
- `DuplicateEvent` — maps to `duplicate_events` table (schema per ADR 0003)

Include:
- `correlation_id` as unique index on `CommunicationLog`
- Indexes on `received_at` and `status`
- Index on `duplicate_events.correlation_id`
- Initial EF Core migration
- Register `NpgsqlDataSource` (singleton) and `DbContext` (scoped) in `Program.cs`
- Call `MigrateAsync()` at startup (per ADR 0002)

**References**: ADR 0002, ADR 0003

---

## Issue 3 — [Feature] Implement CommunicationLogHandler
**Labels**: `feature`, `database`

Replace `DefaultMessageHandler` with `CommunicationLogHandler` implementing `IMessageHandler`:
- Inject `IServiceScopeFactory` to create a scope per message (per ADR 0002)
- Inject `IOptions<RabbitMqOptions>` to populate `source_queue`
- On handle: insert into `communication_log` with `status = 'processed'`, `handled_at = UtcNow`
- On `ON CONFLICT (correlation_id)`: insert into `duplicate_events` within the same transaction
- On exception: insert into `communication_log` with `status = 'failed'`, `error_message` set; rethrow
- Catch `NpgsqlException` where `IsTransient == true`; wrap and throw `TransientPersistenceException` (see Issue 4)

**References**: ADR 0002, ADR 0003

---

## Issue 4 — [Feature] Implement TransientPersistenceException and retry-cap logic
**Labels**: `feature`, `reliability`

Add `TransientPersistenceException` to the connector.

Update `RabbitMqConsumerWorker.OnMessageReceivedAsync`:
- Catch `TransientPersistenceException`
- Read `x-delivery-count` header from `BasicDeliverEventArgs`
- If count < `MaxRetryCount` (from config): NACK with `requeue: true`; log with correlation ID and delivery count
- If count >= `MaxRetryCount`: NACK with `requeue: false` → DLQ; log warning
- All other exceptions: NACK with `requeue: false` → DLQ (existing behavior, no change)

Externalize `MaxRetryCount` in `appsettings.json`; bind via options pattern.

**References**: ADR 0004

---

## Issue 5 — [Feature] Reject messages missing correlation ID
**Labels**: `feature`, `reliability`

Update `RabbitMqConsumerWorker.OnMessageReceivedAsync`:
- Before dispatching to `IMessageHandler`, check if `CorrelationId` is null or empty
- If missing: NACK with `requeue: false` → DLQ immediately
- Add structured log entry: `LogWarning("Message rejected: missing correlation ID. DeliveryTag={DeliveryTag}")`
- Add a metric counter for rejected messages (structured log is sufficient for PoC)

Remove the existing `?? Guid.NewGuid().ToString()` fallback from the worker.

**References**: ADR 0003

---

## Issue 6 — [Infra] Add PostgreSQL health check to the connector
**Labels**: `infrastructure`, `observability`

Add a PostgreSQL connectivity health check:
- Use `AddNpgsql` from `AspNetCore.HealthChecks.Npgsql`
- Connector must report `Unhealthy` when PostgreSQL is unreachable
- Expose via existing health check endpoint (or add one if not present)

**References**: ADR 0002

---

## Issue 7 — [Config] Externalize DB connection string and retry configuration
**Labels**: `configuration`

Ensure all DB-related configuration is externalized:
- `ConnectionStrings:Postgres` — Npgsql connection string
- `Persistence:CommandTimeoutSeconds` — EF Core command timeout
- `Persistence:MaxRetryCount` — retry cap for `x-delivery-count` check (per Issue 4)

Bind via options pattern. No hardcoded values. Document in `appsettings.json` with placeholder values and comments.

---

## Issue 8 — [Test] Unit and integration tests for the persistence layer
**Labels**: `testing`

Add tests covering:
- `CommunicationLogHandler` happy path: message inserted as `'processed'`
- `CommunicationLogHandler` duplicate: `duplicate_events` row inserted, `communication_log` unchanged
- `CommunicationLogHandler` transient Npgsql error: `TransientPersistenceException` thrown
- `RabbitMqConsumerWorker` missing correlation ID: NACK called with `requeue: false`
- `RabbitMqConsumerWorker` transient error below cap: NACK with `requeue: true`
- `RabbitMqConsumerWorker` transient error at cap: NACK with `requeue: false`
- Integration test (Testcontainers): end-to-end insert, verify row in DB, verify duplicate row on second delivery

All 19 existing tests must continue to pass.

---

## Suggested creation order
1 → 2 → 5 → 4 → 3 → 6 → 7 → 8

Dependencies:
- Issue 3 depends on Issue 2 (DbContext) and Issue 4 (TransientPersistenceException)
- Issue 4 depends on Issue 5 (retry-cap logic references missing-ID guard in same method)
- Issue 8 depends on Issues 3, 4, 5
