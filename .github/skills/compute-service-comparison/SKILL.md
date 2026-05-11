---
name: compute-service-comparison
description: "Compare Azure compute services for integration connectors. Use when evaluating Azure Functions Flex Consumption, Azure Functions Premium, AKS, Azure Container Apps, and Azure App Service for availability, operations, cost, security, and migration tradeoffs."
argument-hint: "Describe workload, SLA target, traffic profile, security constraints, and timeline."
user-invocable: true
---

# Compute Service Comparison

Use this skill when you need a structured, repeatable comparison of Azure compute options for connector workloads.

## Scope
- In scope: Azure Functions Flex Consumption, Azure Functions Premium, AKS, Azure Container Apps, Azure App Service.
- Out of scope: deep implementation details, code-level design, and external dependency internals outside your control.

## Inputs
- Workload summary
- SLA and RTO/RPO targets
- Throughput and latency profile
- Team operations maturity
- Security and networking requirements
- Delivery timeline and budget posture

## Procedure
1. Confirm assumptions and dependency boundaries.
2. Compare all five compute options using the same criteria:
   - Availability and resilience
   - Operational complexity
   - Cost and scaling behavior
   - Security and networking posture
   - Data consistency, idempotency, and recovery model
   - Time-to-delivery and migration path
3. Call out what is in SLA scope and out of scope.
4. During the comparison, find at least one relevant and current blog post per compute option.
5. Prefer official Microsoft sources first (Azure Blog, Azure Updates, Microsoft Tech Community), then reputable third-party sources if needed.
6. For each cited blog, include publication date and a one-line relevance note.
7. For any support or limitation statement (for example trigger support, plan support, or feature constraints), place the documentation link inline in the same sentence or table cell where the claim is made.
8. Do not move key support/limitation links into a separate link-only section unless the user explicitly asks for that format.
9. Provide a recommendation plus measurable validation tests.

## Output Format
1. Assumptions
2. Candidate Azure architectures (1-2 options)
3. Reliability and failure-mode analysis
4. Tradeoffs and recommendation
5. Concrete next decisions

## References
- No fixed blog list. Discover sources at comparison time to keep references current.
