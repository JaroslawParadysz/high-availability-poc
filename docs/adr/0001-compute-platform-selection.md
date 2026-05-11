# ADR 0001: Compute Platform Selection for HA Connector

## Status
Accepted (concept phase baseline)

## Date
2026-05-11

## Context
We need to choose the Azure compute platform for a connector that:
- consumes from RabbitMQ,
- persists to a relational database,
- publishes to an external MQTT service.

Target availability is 99.99. The current phase is concept and architecture decisioning.

Key constraints and assumptions:
- Connector service should be stateless to allow horizontal scale and zone resilience.
- External RabbitMQ and external MQTT may be outside our direct operational control.
- At-least-once processing is acceptable if duplicates are handled safely.
- Idempotency for DB writes and MQTT publish is mandatory.
- Reliability patterns (outbox, retries, dead-letter, replay) are part of the architecture regardless of compute platform.

SLA scope definition used by this ADR:
- In scope: connector runtime on Azure, connector-owned data path components on Azure, and connector control plane operations.
- Out of scope: third-party RabbitMQ and MQTT provider availability, internet transit outside Azure control, customer-managed upstream/downstream outages.

Monthly downtime budget for 99.99 availability: 43.2 minutes.

## Decision Drivers
- Ability to support 99.99 service target with zone-resilient topology
- Operational complexity and SRE burden
- Time to deliver concept to POC
- Support for event-driven and always-on connector patterns
- Networking and security controls (private connectivity, managed identity, Key Vault)
- Cost predictability under steady and burst load

## Options Considered
- Azure Functions Flex Consumption
- Azure Functions Premium
- Azure Kubernetes Service (AKS)
- Azure Container Apps
- Azure App Service

## Option Comparison

### Azure Functions Flex Consumption
Pros:
- Fast development and deployment model for event-driven workloads.
- Scale-to-zero can reduce cost for intermittent traffic.

Cons:
- Less suitable for long-running, continuously connected connector workers that maintain broker sessions.
- Cold-start and scale behavior can complicate strict latency and recovery predictability.
- Runtime and networking controls are less explicit than container-first platforms.

99.99 fit:
- Possible for specific trigger patterns, but higher execution-model risk for this connector's always-on consume/persist/publish flow.

### Azure Functions Premium
Pros:
- Pre-warmed instances reduce cold-start impact.
- Better support for always-ready capacity and predictable response.
- Mature serverless operational model with reduced platform management overhead.

Cons:
- More constraints than full container orchestration for protocol-level tuning.
- Can be less straightforward for advanced sidecar patterns and custom runtime dependencies.

99.99 fit:
- Strong candidate if implementation remains function-centric and avoids deep runtime customization.

### Azure Kubernetes Service (AKS)
Pros:
- Maximum control over runtime, scaling policies, protocol libraries, and deployment topology.
- Best fit for advanced resilience patterns, sidecars, and custom networking.
- Supports future expansion to broader polyglot integration workloads.

Cons:
- Highest operational complexity and on-call burden.
- Requires stronger SRE maturity to achieve reliable 99.99 operations.
- Slower concept-to-POC delivery versus managed PaaS options.

99.99 fit:
- Very strong technically, but people/process risk is high unless platform operations are already mature.

### Azure Container Apps
Pros:
- Container-first model with lower operational burden than AKS.
- Good fit for stateless connector services and horizontal scaling.
- Supports revisions, traffic splitting, and modern deployment workflows.
- Better runtime portability than serverless function code models.

Cons:
- Less low-level control than AKS for specialized networking and cluster behavior.
- Platform abstractions may limit some deep tuning scenarios.

99.99 fit:
- Strong balance of reliability capability and operational simplicity for this connector profile.

### Azure App Service
Pros:
- Fast path to deliver and operate with mature PaaS ergonomics.
- Strong developer productivity and straightforward deployment.
- Good fit for web/API style workloads and background workers.

Cons:
- Less container orchestration flexibility than AKS and some Container Apps patterns.
- Scaling and worker-process behavior can be less explicit for queue-centric integration services.

99.99 fit:
- Viable with Premium/Isolated and multi-zone design, but less future-flexible than container-first options for this workload.

## Decision
Select Azure Container Apps as the baseline compute platform for concept-to-POC and first production candidate, with Azure Functions Premium as backup option and AKS as escalation path if control requirements outgrow Container Apps.

## Rationale
- Meets 99.99-oriented architecture needs when deployed with multi-zone resilience, N+1 capacity, health probes, and resilient messaging patterns.
- Reduces operational burden compared to AKS while preserving container-level portability and runtime control.
- Better matches a continuously running connector worker pattern than Flex Consumption.
- Provides faster delivery than AKS and a more future-flexible operating model than App Service/Functions-only implementation.
- Aligns with project goal: explicit failure handling, small testable increments, and fault-injection validation.

## Consequences
Positive:
- Faster concept-to-POC delivery with manageable operational overhead.
- Clear path to implement stateless workers, retries, outbox dispatcher, and horizontal scale.
- Easier migration path to AKS than from tightly coupled serverless code models if requirements evolve.

Negative:
- Some advanced cluster-level controls remain unavailable compared with AKS.
- Team must validate zone-level behavior, scale boundaries, and failover characteristics for target traffic profile.
- Requires disciplined operational design (idempotency, replay, observability) to achieve effective 99.99 outcomes.

Neutral/constraints:
- End-to-end 99.99 remains dependent on external RabbitMQ and MQTT provider reliability.
- Connector SLA and end-to-end SLO must be tracked separately.

## Follow-up Actions
1. Define and approve explicit SLA/SLO documents:
   - connector-internal 99.99 scope,
   - end-to-end SLO including external dependencies.
2. Produce deployment topology spec for Container Apps:
   - zone-redundant environment,
   - minimum replica strategy,
   - autoscaling thresholds,
   - private networking and egress controls.
3. Define idempotency and outbox technical design:
   - unique idempotency keys,
   - transactional DB write + outbox insert,
   - dispatcher retry/backoff/dead-letter rules.
4. Establish observability baseline:
   - correlation IDs across consume, persist, publish,
   - metrics for lag, retries, duplicate detection, dead-letter rate,
   - alert thresholds and error-budget burn alerts.
5. Run fault-injection validation in POC:
   - container restart/eviction,
   - database failover simulation,
   - MQTT target outage,
   - RabbitMQ connectivity degradation.
6. Define AKS migration trigger criteria (objective thresholds):
   - required runtime/network controls not available in Container Apps,
   - sustained scale/latency needs exceeding validated limits,
   - compliance constraints requiring deeper platform control.

## References
- Azure Functions Flex Consumption overview: https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan
- Azure Functions Premium plan: https://learn.microsoft.com/azure/azure-functions/functions-premium-plan
- Azure Kubernetes Service (AKS) reliability guidance: https://learn.microsoft.com/azure/aks/reliability-aks
- Azure Container Apps overview: https://learn.microsoft.com/azure/container-apps/overview
- Azure App Service reliability guidance: https://learn.microsoft.com/azure/app-service/overview-reliability
