# ADR 0002: Persistence Store Selection for the Connector

## Status
Accepted (concept phase)

## Date
2026-05-13

## Context
The connector processes messages from RabbitMQ and currently has no durable storage. We need a relational persistence store to record every consumed message for audit, replay investigation, and duplicate detection.

Key constraints:
- .NET / C# runtime (existing codebase).
- Deployed on Azure (Container Apps, per ADR 0001).
- Single connector instance in the current phase; multi-instance and DB HA are deferred to the infrastructure phase.
- Schema must support idempotency enforcement at the DB constraint level.

## Decision Drivers
- Developer productivity and maturity of the .NET driver ecosystem.
- Schema evolution support (migrations as code).
- HA story for future Azure-hosted production deployment.
- Open-source preference; avoid vendor lock-in at the ORM layer.

## Options Considered

### Database
| Option | Notes |
|--------|-------|
| PostgreSQL 15+ | Open-source, JSONB for flexible payloads, mature Npgsql driver, strong Azure HA story (Flexible Server with zone-redundant standby) |
| Azure SQL / SQL Server | Strong Azure integration, more licensing cost, less flexible for future polyglot scenarios |
| SQLite | Zero-infra for local dev only; not viable for multi-instance or production HA |

### ORM
| Option | Notes |
|--------|-------|
| Entity Framework Core 10 | .NET standard, migrations first-class, Npgsql provider mature, good async support |
| Dapper | Lightweight, but requires manual migration management |
| Raw Npgsql | Maximum control, highest boilerplate |

## Decision
- **Database**: PostgreSQL 15+
- **ORM**: Entity Framework Core 10 with Npgsql provider
- **Connection pooling**: NpgsqlDataSource (built-in pooling); registered as singleton
- **Migrations**: `MigrateAsync()` at app startup for the current single-instance phase; must be replaced with a dedicated migration job before multi-instance production deployment to avoid startup races
- **DbContext lifetime**: Scope-per-message via `IServiceScopeFactory` — worker is singleton-scoped; EF Core DbContext must be scoped per unit of work to avoid change-tracking state corruption across messages

## Rationale
- PostgreSQL provides the strongest combination of open-source flexibility, JSONB payload storage, and a proven Azure HA story for future production.
- EF Core 10 reduces boilerplate, provides type-safe migrations, and the Npgsql provider is the de-facto standard for PostgreSQL in .NET.
- `IServiceScopeFactory` scope-per-message is the correct pattern for injecting scoped services (DbContext) into singleton background workers; direct injection would cause state corruption.
- `MigrateAsync()` at startup is acceptable for the PoC single-instance deployment; the risk of startup races under multi-instance scale is a known and deferred concern.

## Consequences

### Positive
- Schema changes are version-controlled and reversible via EF Core migrations.
- NpgsqlDataSource provides efficient connection pooling without additional infrastructure.
- Connector is ready for Azure Database for PostgreSQL Flexible Server (zone-redundant) when the infrastructure phase begins.

### Negative
- `MigrateAsync()` at startup must be explicitly replaced before scaling to multiple instances.
- EF Core adds a dependency and abstraction layer; for simple insert-heavy workloads, raw SQL would be leaner.

### Neutral / Constraints
- Full 99.99% pipeline availability requires DB HA (Azure Database for PostgreSQL Flexible Server with zone-redundant standby). This ADR covers connector-side design only.
- Local development uses a PostgreSQL container via docker-compose.

## Follow-up Actions
1. Add PostgreSQL container to docker-compose with health checks.
2. Implement `ConnectorDbContext` with `CommunicationLog` and `DuplicateEvent` entities.
3. Add initial EF Core migration.
4. Register `NpgsqlDataSource` and `DbContext` in `Program.cs`.
5. Call `MigrateAsync()` at startup.
6. Add PostgreSQL connectivity health check.

## References
- `docs/research/database-persistence-concept.md`
- ADR 0001: Compute Platform Selection
- Npgsql EF Core provider: https://www.npgsql.org/efcore/
- Azure Database for PostgreSQL Flexible Server reliability: https://learn.microsoft.com/azure/postgresql/flexible-server/concepts-high-availability
