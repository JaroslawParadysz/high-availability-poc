# Roadmap: Concept to POC for HA Connector

## Phase 1: Requirements Clarification

Outcome:
- A signed-off reliability scope and measurable non-functional requirements.

Tasks:
- Confirm SLA scope boundaries (internal only vs end-to-end).
- Define throughput profile (average, peak, burst).
- Define latency SLO and max retry windows.
- Define RTO and RPO targets.

Exit criteria:
- Approved requirement note and risk register.

## Phase 2: Reference Architecture Freeze

Outcome:
- One selected architecture option and deployment topology.

Tasks:
- Choose compute platform (initially App Service recommended).
- Choose relational engine (PostgreSQL recommended unless strong SQL Server requirement).
- Select region strategy (single region multi-zone first; evaluate multi-region).
- Define networking model (private endpoints, egress strategy, DNS).

Exit criteria:
- ADR set accepted and versioned.

## Phase 3: Reliability Design

Outcome:
- Robust message processing model with failure recovery.

Tasks:
- Define idempotency model and keys.
- Define outbox schema and dispatcher retry policy.
- Define dead-letter and replay process.
- Define backpressure and circuit-breaker thresholds.

Exit criteria:
- Sequence diagrams and failure-mode table complete.

## Phase 4: POC Build

Outcome:
- Running end-to-end flow in Azure non-production.

Tasks:
- Build minimal connector service.
- Configure PostgreSQL HA and schema.
- Integrate RabbitMQ consumer and MQTT publisher.
- Implement structured logs, core metrics, and alerts.

Exit criteria:
- End-to-end functional demo with fault injection results.

## Phase 5: Validation Against 99.99

Outcome:
- Evidence that design can meet target in defined scope.

Tasks:
- Run resilience tests: instance restarts, DB failover simulation, network degradation.
- Track SLO indicators and error budget burn.
- Document known gaps and mitigations.

Exit criteria:
- Go or no-go recommendation with quantified risk.

## Phase 6: Production Readiness Planning

Outcome:
- Detailed backlog for hardening and launch.

Tasks:
- Security review and threat model.
- Runbook and incident response playbooks.
- Cost model and capacity plan.
- Deployment strategy (blue-green or canary).

Exit criteria:
- Approved production readiness checklist.
