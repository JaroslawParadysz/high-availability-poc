# Architecture Concept: RabbitMQ to Relational DB to MQTT on Azure

## 1. Scope and Assumptions

Assumptions for initial concept:
- Connector is stateless or near-stateless, so horizontal scaling is feasible.
- RabbitMQ is an external dependency and may be outside Azure.
- External MQTT endpoint is a third-party service outside our control.
- At-least-once message delivery is acceptable.
- Duplicate processing must be handled safely (idempotent writes and idempotent publishes).

Assumptions to validate with customer:
- SLA scope: Does 99.99 apply to connector API only, end-to-end transaction, or include external dependencies?
- Data loss tolerance: maximum acceptable message loss and replay window.
- Recovery targets: RTO and RPO requirements.
- Peak throughput and message size.
- Compliance constraints for data residency and encryption.

## 2. Functional Flow

1. Connector subscribes to RabbitMQ topic or queue binding.
2. Message is validated and assigned a deterministic idempotency key.
3. Connector stores payload and processing state in relational database.
4. Connector publishes event to MQTT external service.
5. Connector marks message outcome and emits metrics/logs.

Recommended reliability pattern:
- Use the outbox pattern in relational DB for MQTT publish reliability.
- Perform DB transaction first, then asynchronous outbox dispatcher sends MQTT.
- Retries use exponential backoff with dead-letter handling.

## 3. Azure Service Options

## Option A: App Service + Azure Database for PostgreSQL Flexible Server

Suggested components:
- Compute: Azure App Service (Linux) in Premium v3 or isolated tier.
- Database: Azure Database for PostgreSQL Flexible Server (zone redundant HA).
- Cache/coordination: Optional Azure Cache for Redis for distributed locks and short-lived dedupe cache.
- Secrets: Azure Key Vault.
- Observability: Azure Monitor, Log Analytics, Application Insights.
- Network: VNet integration, private endpoints, NSGs.

Pros:
- Fast to deliver with strong platform support.
- Lower operational complexity than Kubernetes.
- Good fit for connector workloads and autoscaling.

Cons:
- Less control over runtime and sidecars than AKS.
- Advanced networking and custom protocol tuning can be more constrained.

## Option B: AKS + Azure Database for PostgreSQL Flexible Server

Suggested components:
- Compute: AKS with at least 3 nodes across availability zones.
- Database: Azure Database for PostgreSQL Flexible Server (zone redundant HA).
- Messaging runtime: containerized worker service for RabbitMQ and MQTT.
- Secrets: Azure Key Vault with CSI driver.
- Observability: Azure Monitor Container Insights + Prometheus/Grafana if needed.
- Network: Azure CNI, private cluster, egress controls via Azure Firewall.

Pros:
- Maximum runtime flexibility and deployment control.
- Better for future growth into polyglot workloads and custom operators.

Cons:
- Higher operational overhead.
- More SRE maturity required to reliably hit 99.99.

## Recommended starting point:
- Option A for concept-to-POC speed, then re-evaluate AKS if scale or custom control needs emerge.

## 4. Availability Strategy for 99.99

Design principles:
- Multi-zone deployment for all critical Azure components.
- Stateless connector instances with N+1 capacity.
- Retry, timeout, and circuit-breaker policies for RabbitMQ and MQTT operations.
- Backpressure and bounded queues to avoid cascading failures.
- Dead-letter and replay mechanism for poison or exhausted messages.

Data integrity and idempotency:
- Unique idempotency key on business event id.
- Upsert semantics or transactional merge for writes.
- Outbox table with exactly-once handoff semantics per consumer id.

Failure handling:
- If DB unavailable: pause acknowledgment and retry with bounded buffer.
- If MQTT unavailable: persist outbox and continue retries asynchronously.
- If RabbitMQ unavailable: reconnect with jittered backoff and health-state downgrade.

## 5. SLA Considerations and Caveats

Important caveat:
- End-to-end 99.99 is only realistic if external dependencies (RabbitMQ source and MQTT target) are also designed or contracted for comparable availability.

Working model:
- Define two SLAs:
  - Internal platform SLA (connector + Azure data plane).
  - End-to-end transaction SLO including third-party dependencies.

Illustrative monthly downtime budget for 99.99:
- 43.2 minutes per month.

## 6. Security and Compliance Baseline

- Managed identities for all Azure resource access.
- Key Vault for all credentials and certificates.
- Private networking where possible; avoid public ingress by default.
- TLS for all in-transit links, including RabbitMQ and MQTT endpoints.
- Audit logging for message processing outcomes and admin operations.

## 7. Observability Baseline

Metrics:
- Throughput: consumed messages per second, published MQTT per second.
- Latency: consume-to-persist, persist-to-publish, end-to-end latency.
- Reliability: retry count, dead-letter count, duplicate-detected count.
- Saturation: CPU, memory, queue lag, DB connection pool usage.

Alerts:
- Queue lag above threshold for sustained duration.
- Dead-letter rate above threshold.
- Error budget burn rate exceeding target.
- DB failover or connection exhaustion events.

## 8. Open Questions to Close Before Build

1. Is exactly-once delivery mandatory end-to-end, or is effectively-once sufficient?
2. Which relational engine is preferred: PostgreSQL or Azure SQL?
3. Must RabbitMQ run in Azure, or is it customer-managed externally?
4. Are there strict latency targets for the MQTT event publication?
5. Is active-active across two Azure regions required, or active-passive is acceptable?
