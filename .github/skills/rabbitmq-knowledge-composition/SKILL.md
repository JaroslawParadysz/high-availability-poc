---
name: rabbitmq-knowledge-composition
description: "Compose RabbitMQ knowledge documents for .NET C# programmers using strict guardrails for concepts, practical C# implementation approaches, reliability analysis, and best practices."
argument-hint: "Provide topic, audience level (.NET C# programmer), Azure deployment context, constraints, and desired depth."
user-invocable: true
---

# RabbitMQ Knowledge Composition Guardrails

Use this skill to produce consistent, high-quality RabbitMQ knowledge documents for .NET C# programmers building real projects.

## Purpose
- Convert RabbitMQ questions into structured, .NET C# programmer-friendly guidance.
- Prioritize concept understanding, practical C# implementation approaches, and production-ready best practices using the RabbitMQ.Client library.
- Keep outputs explicit about assumptions, reliability implications, and operational boundaries.

## Scope
- In scope: RabbitMQ concepts, queue and exchange patterns, routing models, consumer behavior, delivery guarantees, reliability patterns, failure handling, and operational guidance — all from a .NET C# perspective.
- In scope: practical implementation advice using RabbitMQ.Client (official .NET client), IHostedService / BackgroundService patterns, dependency injection, and Azure hosting.
- Out of scope: other languages or runtimes, full turn-key production code, broad rewrites, and unsupported certainty when data is missing.

## Required Inputs
- Topic or question
- Audience level (beginner, intermediate, advanced .NET C# programmer)
- Azure deployment context (for example, single-region Container Apps, AKS, multi-region active-active)
- Constraints (SLA, latency, compliance, cost, timeline, team maturity)

If any required input is missing, state assumptions before proceeding.

## Non-Negotiable Guardrails
1. Start with assumptions and dependency boundaries.
2. Explicitly separate what is in SLA scope and out of scope.
3. Treat idempotency, retries, dead-letter handling, and replay strategy as first-class concerns for cross-system flows.
4. Include failure modes and recovery behavior, not only happy-path design.
5. Distinguish facts from recommendations.
6. If confidence is limited, say so and list unknowns.
7. Do not invent unsupported product limits, guarantees, or feature support.
8. Keep guidance incremental and testable; avoid large speculative redesigns.
9. Anchor implementation guidance to .NET C# patterns: RabbitMQ.Client API, async/await, IConnection/IChannel lifecycle, BackgroundService, and xUnit or NUnit test approaches.
10. Prefer clear "when to use" and "when to avoid" guidance for each recommendation.
11. Add source links for factual statements and product capability claims.
12. Place each source link inline next to the related statement, sentence, bullet, or table cell.
13. Prefer official sources first (RabbitMQ docs, GitHub RabbitMQ repos, Microsoft docs for Azure/.NET behavior).
14. If no trustworthy source is available, mark the statement as an assumption instead of presenting it as fact.

## Procedure
1. Restate the objective in one sentence.
2. Declare assumptions and unknowns.
3. Define context and boundaries:
   - System boundary
   - External dependencies outside direct control
   - SLA in-scope and out-of-scope items
4. Explain the RabbitMQ concept or option set clearly for the audience level.
5. Add practical .NET C# implementation approach:
   - Typical topology and message flow
   - Producer and consumer responsibilities
   - RabbitMQ.Client API usage (IConnection, IChannel, BasicConsume, BasicAck)
   - Acknowledgment and retry model in C# code
   - Idempotency and duplicate handling strategy
   - DI registration, IHostedService / BackgroundService integration
6. Analyze reliability and failure modes:
   - Message loss, duplication, reordering
   - Consumer crash and restart behavior
   - Backpressure, queue lag, saturation risk
   - Poison message handling and dead-letter policy
7. Provide best practices and anti-patterns specific to .NET C#:
   - What to do by default
   - What to avoid (for example, sharing channels across threads, blocking async calls)
   - Common pitfalls in production .NET services
8. Add source links inline for factual claims and capability statements in all sections.
9. Provide tradeoffs and a practical recommendation.
10. Add concrete validation checks (fault-injection or xUnit/NUnit tests).
11. End with open questions and next decisions.

## Output Template
1. Objective
2. Assumptions
3. In Scope and Out of Scope
4. Key RabbitMQ Concepts
5. Practical .NET C# Implementation Approach
6. Best Practices and Anti-Patterns (.NET C#)
7. Reliability and Failure-Mode Analysis
8. Tradeoffs
9. Recommendation
10. Validation Plan (fault-injection and xUnit/NUnit tests)
11. Open Questions
12. Sources (optional rollup list; do not replace inline links)

## Citation Rules
- Every factual claim should have a source link adjacent to that claim.
- Keep links in the same line or bullet where the claim appears.
- In tables, place links in the same cell as the claim.
- Use stable documentation links over blog summaries when possible.
- If a recommendation is opinionated, cite the supporting fact and label the recommendation as judgment.

## Quality Checklist
- Does the document state assumptions explicitly?
- Is SLA scope separation clear?
- Is the guidance grounded in .NET C# and RabbitMQ.Client APIs?
- Are best practices paired with anti-pattern warnings specific to .NET?
- Are failure modes and recovery paths concrete?
- Are tradeoffs balanced instead of one-sided?
- Is the recommendation actionable and testable with xUnit or NUnit?
- Are unknowns and risks visible?
- Do factual claims and capability statements include inline source links?
- Are official docs preferred where available?

## Optional Depth Modes
- Quick: concise concept explanation, one .NET C# implementation approach, top risks, and one recommendation.
- Standard: full template with practical .NET C# guidance and validation checks.
- Deep: full template plus alternative patterns, decision matrix, sample RabbitMQ.Client code snippets, and operational rollout guidance.

## Preferred Topic Coverage (When Relevant)
- Exchange types and routing key strategy
- Queue design (durable, exclusive, quorum vs classic)
- Publisher confirms and message persistence in C#
- Consumer acknowledgments, BasicQos prefetch, and concurrency with IChannel
- Retry and dead-letter exchange patterns in .NET
- Idempotent consumer design using C# middleware or decorators
- IConnection and IChannel lifecycle management (single vs pooled)
- BackgroundService and IHostedService consumer patterns
- Async consumer with AsyncEventingBasicConsumer
- Schema and contract evolution (for example System.Text.Json, Protobuf)
- Testing strategies: xUnit with Testcontainers for RabbitMQ, fault injection
- Observability in .NET: structured logging (Serilog/Microsoft.Extensions.Logging), metrics (OpenTelemetry)