# Copilot Instructions

This workspace is for designing and implementing a high-availability connector concept on Azure.

## Current Project Context
- Primary flow: consume from RabbitMQ, persist to a relational database, then publish an event to an external MQTT service.
- Current phase: concept and architecture decisioning first, implementation second.
- Customer target: 99.99 availability.
- Cloud platform: Azure for connector runtime and relational database.
- Assume external dependencies (RabbitMQ source and MQTT target) may be outside our direct operational control unless explicitly stated.

## Goals
- Prioritize reliability, resilience, and clear failure handling.
- Keep architecture decisions explicit and traceable.
- Prefer small, testable increments over large rewrites.
- Prefer designs that can be validated through fault-injection experiments.

## Working Style
- Before coding, summarize assumptions and expected behavior.
- Propose a short plan for multi-step changes.
- Keep commits and diffs focused on one concern.
- Do not make unrelated refactors.
- In concept phase, default to architecture notes, ADRs, diagrams, and decision matrices.
- Explicitly state what is in SLA scope and what is out of scope.

## Architecture Preferences
- Favor loosely coupled components with clear interfaces.
- Design for graceful degradation and recovery.
- Include health checks, retries, and idempotency where relevant.
- Document tradeoffs for consistency, availability, and partition tolerance.
- Use reliable messaging patterns (outbox, dead-letter handling, replay flow) when crossing system boundaries.
- Prefer stateless connector services to support horizontal scale and zone-level resilience.
- Treat idempotency as a first-class requirement for database writes and MQTT publish operations.

## Azure Decision Guidance
- Prefer managed Azure services when they meet requirements.
- Evaluate App Service, AKS, Azure Container Apps, Azure Functions Premium, and Azure Functions Flex Consumption on equal footing; do not default to App Service over AKS or any other platform without explicit tradeoff analysis.
- Use availability zones for critical services and document single-region versus multi-region tradeoffs.
- Keep networking and security explicit: private endpoints, managed identity, Key Vault, least privilege.

## Code Quality
- Add or update tests with each meaningful change.
- Include concise comments only where logic is non-obvious.
- Avoid introducing new dependencies unless justified.
- Keep configuration externalized and environment-specific.
- For reliability-sensitive logic, include failure-mode tests and retry/idempotency tests.

## Observability
- Add structured logging at key boundaries.
- Include basic metrics for throughput, latency, errors, and saturation.
- Define expected alert conditions when introducing critical paths.
- Track queue lag, retry counts, dead-letter rate, duplicate detection, and error-budget burn.
- Include correlation IDs across consume, persist, and publish stages.

## Security and Safety
- Do not hardcode secrets, tokens, or credentials.
- Validate external input and fail safely.
- Prefer least-privilege defaults.
- Enforce TLS for all external connections, including RabbitMQ and MQTT.

## Documentation Expectations
- Update README and architecture notes when behavior changes.
- For major decisions, add a short decision note with context, options, and rationale.
- Keep an ADR trail for availability model, data store choice, and failure-handling strategy.
- Record assumptions, risks, and open questions in each concept document.

## Collaboration
- If requirements are ambiguous, ask targeted clarification questions.
- When blocked, present 1-2 viable alternatives with pros and cons.

## Output Expectations for Copilot
- For concept questions, provide:
	- assumptions,
	- 1-2 architecture options,
	- reliability and failure-mode analysis,
	- tradeoffs and recommendation,
	- concrete next decisions.
- For implementation tasks, include test strategy and operational impact.
- Avoid broad rewrites unless explicitly requested.
