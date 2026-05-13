# High Availability Connector Concept

This repository is used to design a high-availability connector architecture on Azure.

## Problem Statement

Build a connector that:
- Consumes messages from RabbitMQ.
- Persists business data in a relational database.
- Publishes follow-up events via MQTT to an external service.

Customer target: 99.99% SLA for the solution.

## Current Phase

Concept and architecture decisions only.

## Documents

- docs/architecture-concept.md: candidate architectures, service selection, reliability strategy.
- docs/adr/0001-initial-azure-platform-choice.md: first architecture decision record.
- docs/roadmap-concept-to-poc.md: phased plan from concept to proof of concept.

## Next Action

Review open questions in docs/architecture-concept.md and confirm constraints so we can lock the target architecture.

## Local development (Docker Compose)

Set the PostgreSQL variables before starting containers:

- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `POSTGRES_DB`

Example:

```bash
export POSTGRES_USER=connector_dev
export POSTGRES_PASSWORD=change_me
export POSTGRES_DB=connector
```

Then run:

`docker compose up -d`

> This setup is intended for local development. Do not use plaintext credentials in production environments.
