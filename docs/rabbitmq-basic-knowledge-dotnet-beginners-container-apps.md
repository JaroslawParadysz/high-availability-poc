# RabbitMQ Basic Knowledge for .NET C# Developers on Azure Container Apps

**Depth mode:** Deep  
**Audience:** Beginner .NET C# programmer  
**Azure context:** Azure Container Apps (single-region, multi-zone)  
**Date:** 2026-05-12

---

## 1. Objective

Provide a beginner-friendly reference for understanding RabbitMQ core concepts and building a reliable .NET C# consumer service on Azure Container Apps, grounded in the HA Connector project's requirements: at-least-once delivery, idempotent DB writes, 99.99 availability target, and the `RabbitMQ.Client` 7.x async API.

---

## 2. Assumptions

| # | Assumption | Basis |
|---|-----------|-------|
| A1 | RabbitMQ is an external dependency potentially outside Azure; the connector does not own or operate the broker. | [architecture-concept.md](architecture-concept.md) |
| A2 | At-least-once delivery is acceptable; duplicates must be handled safely via idempotent writes. | [architecture-concept.md](architecture-concept.md) |
| A3 | The connector uses **RabbitMQ.Client 7.1.2** targeting **net10.0**. | [Connector.csproj](../src/Connector/Connector.csproj) |
| A4 | Consumer workers run as Azure Container Apps replicas in a multi-zone environment. | [ADR 0001](adr/0001-compute-platform-selection.md) |
| A5 | The RabbitMQ broker runs version 3.12 or later (quorum queues and streams available). **Assumption** — validate with broker operator. | — |
| A6 | TLS is required for all broker connections; port 5671 signals TLS-enabled connections. | [architecture-concept.md](architecture-concept.md) |
| A7 | Credentials and connection strings are never hard-coded; they are supplied via environment variables or Azure Key Vault secret references. | [Azure Container Apps — Manage secrets](https://learn.microsoft.com/azure/container-apps/manage-secrets); [Azure Key Vault overview](https://learn.microsoft.com/azure/key-vault/general/overview) |

**Unknowns that affect design:**

- Whether the broker operator already enforces quorum queues or still uses classic mirrored queues.
- Whether `MessageId` or a stable business key is set by upstream producers (impacts idempotency key strategy).
- Peak message throughput (messages/second) — needed to size prefetch count and replica count.

---

## 3. In Scope and Out of Scope

### In Scope

- RabbitMQ core concepts: broker, connection, channel, exchange, queue, binding, routing key, message properties.
- Consumer-side delivery guarantees: acknowledgments, negative acknowledgments, prefetch (`BasicQos`), and dead-letter handling.
- .NET C# implementation using `RabbitMQ.Client` 7.x async API (`IConnection`, `IChannel`, `AsyncEventingBasicConsumer`) and `BackgroundService`.
- Reliability patterns: reconnect with back-off, at-least-once processing, idempotency, DLX, and replay.
- Observability: structured logging, correlation IDs, and metric collection points.
- Validation using xUnit and Testcontainers.

### Out of Scope

- Operating or administering the RabbitMQ broker (cluster setup, policies, user management).
- Publisher confirms and mandatory flags (connector is a pure consumer in this project phase).
- Exactly-once delivery end-to-end (AMQP 0-9-1 does not provide this; additional coordination layer required).
- RabbitMQ Streams (separate protocol; relevant for high-fan-out replay in future phases).
- Multi-region active-active broker topology (future phase).

### SLA Boundary

| Component | In SLA scope | Notes |
|-----------|:-----------:|-------|
| Connector worker (Container Apps) | ✅ | Target: 99.99 for connector runtime |
| Azure Container Apps platform | ✅ | Multi-zone environment required |
| Azure-managed data plane (PostgreSQL, Key Vault) | ✅ | |
| RabbitMQ broker (external) | ❌ | External dependency; track separately as SLO input |
| MQTT target service (external) | ❌ | External dependency; tracked separately |
| Internet transit outside Azure | ❌ | Not in connector SLA scope |

---

## 4. Key RabbitMQ Concepts

### 4.1 What Is RabbitMQ?

RabbitMQ is an open-source message broker that implements the **AMQP 0-9-1** protocol [(RabbitMQ protocol guide)](https://www.rabbitmq.com/docs/protocol). It decouples producers (applications that publish messages) from consumers (applications that receive and process them) by acting as an intermediary buffer.

Think of it as a post office: the sender drops a letter into a mailbox (exchange), the post office routes it to the correct P.O. box (queue), and the recipient picks it up when ready — independently of when the sender posted it.

### 4.2 Broker

The **broker** is the RabbitMQ server process. It accepts connections, stores messages in queues, routes them via exchanges, and tracks acknowledgment state [(RabbitMQ clustering guide)](https://www.rabbitmq.com/docs/clustering). Production brokers run as a cluster of three or more nodes for resilience.

### 4.3 Virtual Host (vhost)

A **virtual host** is a logical namespace inside a single broker. It separates exchanges, queues, users, and permissions [(RabbitMQ vhost docs)](https://www.rabbitmq.com/docs/vhosts). Different environments (dev, staging, prod) can share one broker by using distinct vhosts. The connector defaults to vhost `"/"` ([`RabbitMqOptions.cs`](../src/Connector/RabbitMqOptions.cs)).

### 4.4 Connection

A **connection** is a TCP connection between a .NET client and the broker. Creating a connection is expensive: it involves a TCP handshake, optional TLS negotiation, and AMQP protocol handshake [(RabbitMQ connections guide)](https://www.rabbitmq.com/docs/connections). Each running connector instance must maintain **one long-lived connection** and reconnect on failure.

In `RabbitMQ.Client` 7.x the type is `IConnection`, obtained via `ConnectionFactory.CreateConnectionAsync()`:

```csharp
// Create once per process lifetime. Store as a private field on the BackgroundService.
IConnection connection = await factory.CreateConnectionAsync(cancellationToken);
```

### 4.5 Channel

A **channel** is a lightweight virtual connection multiplexed over a single TCP connection [(RabbitMQ channels guide)](https://www.rabbitmq.com/docs/channels). All AMQP operations — declare, consume, ack — happen on a channel.

In `RabbitMQ.Client` 7.x the type is `IChannel` (renamed from `IModel` in 6.x). **Each `IChannel` must be used by only one thread at a time** — sharing a channel across concurrent callbacks is the single most common threading mistake in .NET consumers [(RabbitMQ .NET client guide)](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).

```csharp
// One channel per logical consumer. Never share across threads.
IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
```

### 4.6 Exchange

An **exchange** receives messages from producers and routes them to zero or more queues based on routing rules [(RabbitMQ exchanges guide)](https://www.rabbitmq.com/docs/exchanges).

| Exchange type | Routing behaviour | When to use |
|--------------|-------------------|-------------|
| **Direct** | Routes by exact routing key match [(docs)](https://www.rabbitmq.com/docs/exchanges#direct) | Point-to-point command routing |
| **Fanout** | Broadcasts to all bound queues; ignores routing key [(docs)](https://www.rabbitmq.com/docs/exchanges#fanout) | Notifications to all subscribers |
| **Topic** | Routes by wildcard-pattern routing key (`*` = one word, `#` = zero or more) [(docs)](https://www.rabbitmq.com/docs/exchanges#topic) | Event categories, flexible routing |
| **Headers** | Routes by message header attributes [(docs)](https://www.rabbitmq.com/docs/exchanges#headers) | Complex conditional routing (rare) |
| **Default (nameless)** | Routes to the queue whose name equals the routing key [(docs)](https://www.rabbitmq.com/docs/exchanges#default-exchange) | Simple direct-to-queue delivery |

### 4.7 Queue

A **queue** is a buffer that stores messages until a consumer retrieves them [(RabbitMQ queues guide)](https://www.rabbitmq.com/docs/queues).

Key queue properties:

| Property | Meaning | Recommendation |
|----------|---------|----------------|
| **Durable** | Queue survives broker restart [(docs)](https://www.rabbitmq.com/docs/queues#durability) | Always `true` in production |
| **Auto-delete** | Queue deleted when last consumer disconnects | `false` for persistent workloads |
| **Exclusive** | Only one connection may use this queue | `false` for shared consumer pools |
| **Type** | Classic or Quorum | **Quorum** for production [(quorum queue docs)](https://www.rabbitmq.com/docs/quorum-queues) |

#### Classic vs Quorum Queues

| Characteristic | Classic queue | Quorum queue |
|---------------|--------------|--------------|
| Replication | Optional mirroring (deprecated in 3.12+) [(deprecation notice)](https://www.rabbitmq.com/blog/2021/08/21/4.0-deprecation-announcements) | Raft-based, always replicated |
| Data safety on node failure | Risk of loss without mirroring configured | Designed for durability with majority-write guarantee |
| Throughput | Higher at low concurrency | Slightly lower; more predictable latency under load |
| Recommended for new projects | ❌ | ✅ |

### 4.8 Binding

A **binding** links an exchange to a queue, optionally with a routing key or binding arguments [(RabbitMQ bindings)](https://www.rabbitmq.com/docs/bindings). A message arriving at the exchange is copied to every queue whose binding matches.

### 4.9 Message Properties

A **message** consists of a **body** (raw bytes) and **properties** (metadata) [(AMQP 0-9-1 model — message properties)](https://www.rabbitmq.com/tutorials/amqp-concepts#message-properties).

| Property | Purpose | Notes for this project |
|----------|---------|------------------------|
| `CorrelationId` | Track a message across systems | Connector reads this for log correlation scope |
| `MessageId` | Unique message identifier | Use as idempotency key if set by producer |
| `DeliveryMode` | `1` = transient, `2` = persistent | Always `2` for durable-queue messages in production |
| `ContentType` | MIME type of body | Recommend `application/json` |

### 4.10 Acknowledgments

After a consumer receives a message it must tell the broker what happened [(RabbitMQ consumer acknowledgements)](https://www.rabbitmq.com/docs/confirms#acknowledgement-modes):

| Signal | `RabbitMQ.Client` method | Effect |
|--------|--------------------------|--------|
| **Ack** | `BasicAckAsync` | Message removed from queue; considered delivered. |
| **Nack** | `BasicNackAsync` | Returned to queue (`requeue: true`) or routed to DLX (`requeue: false`). |
| **Reject** | `BasicRejectAsync` | Same as nack but for single messages only. |

**Auto-ack mode** (`autoAck: true`) removes the message the moment it is delivered — before the consumer has processed it. **Never use `autoAck: true` for reliability-sensitive workloads** [(RabbitMQ docs warning)](https://www.rabbitmq.com/docs/confirms#acknowledgement-modes).

### 4.11 Prefetch (`BasicQos`)

**Prefetch** limits how many unacknowledged messages the broker will push to a consumer at once. Without a prefetch limit, the broker floods a slow consumer [(RabbitMQ consumer prefetch)](https://www.rabbitmq.com/docs/consumer-prefetch):

```csharp
// Allow at most 10 unacknowledged messages per channel before waiting for acks.
await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken);
```

`global: false` applies the limit per-consumer on this channel [(API guide)](https://www.rabbitmq.com/client-libraries/dotnet-api-guide). `global: true` applies it across all consumers sharing the channel.

### 4.12 Dead-Letter Exchange (DLX)

A **dead-letter exchange** (DLX) is where the broker routes messages that cannot be delivered — either nack'd with `requeue: false`, expired (TTL exceeded), or overflow-rejected [(RabbitMQ DLX docs)](https://www.rabbitmq.com/docs/dlx).

Pattern used in this project:
1. Set `x-dead-letter-exchange` argument on the main inbound queue at declaration time.
2. Bind a separate `connector.dead-letter` queue to that DLX.
3. A monitoring or replay consumer reads the DLX queue and either alerts, retries with a delay, or archives messages for manual review.

---

## 5. Practical .NET C# Implementation Approach

### 5.1 Topology and Message Flow

```
[RabbitMQ Broker]
    ├─ Exchange: "connector.topic" (topic type)
    │       └─ Binding: routing key "order.#"
    │
    ├─ Queue: "connector.inbound" (durable, quorum)
    │       x-dead-letter-exchange: "connector.dlx"
    │
    └─ Queue: "connector.dead-letter" (durable, bound to "connector.dlx")

[Connector Worker — Azure Container Apps replica]
    │
    ├─ 1. BasicConsumeAsync("connector.inbound", autoAck: false)
    ├─ 2. OnMessageReceivedAsync → validate → assign idempotency key
    ├─ 3. Upsert DB record + insert outbox row (single transaction)
    ├─ 4. BasicAckAsync on success
    └─ 5. BasicNackAsync(requeue: false) on unrecoverable failure → DLX
```

### 5.2 `ConnectionFactory` and TLS Configuration

```csharp
var factory = new ConnectionFactory
{
    HostName    = _options.Host,
    Port        = _options.Port,
    VirtualHost = _options.VirtualHost,
    UserName    = _options.Username,   // injected from config/Key Vault — never hard-coded
    Password    = _options.Password,   // injected from config/Key Vault — never hard-coded
    Ssl = new SslOption
    {
        Enabled    = _options.Port == 5671,  // TLS enforced on AMQPS port
        ServerName = _options.Host,
    },
};

// One long-lived connection per BackgroundService instance.
_connection = await factory.CreateConnectionAsync(cancellationToken);
_channel    = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
```

`RabbitMQ.Client` 7.x uses a fully async API and replaces the 6.x `IModel` type with `IChannel` [(RabbitMQ.Client 7.0 release notes)](https://github.com/rabbitmq/rabbitmq-dotnet-client/releases/tag/v7.0.0).

### 5.3 `BackgroundService` Consumer Pattern

`BackgroundService` (implementing `IHostedService`) keeps the consumer alive for the lifetime of the process [(Microsoft docs — BackgroundService)](https://learn.microsoft.com/dotnet/core/extensions/background-service):

```csharp
public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private IConnection? _connection;
    private IChannel?    _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Connector starting. Queue={Queue}", _options.QueueName);
        await ConnectWithRetryAsync(stoppingToken);

        // Keep-alive loop: reconnect if the connection drops unexpectedly.
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_connection is null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting…");
                await ConnectWithRetryAsync(stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
```

Register in `Program.cs`:

```csharp
services.Configure<RabbitMqOptions>(context.Configuration.GetSection("RabbitMq"));
services.AddHostedService<RabbitMqConsumerWorker>();
```

### 5.4 Consumer Registration and Prefetch

```csharp
private async Task ConnectWithRetryAsync(CancellationToken ct)
{
    int attempt = 0;
    while (!ct.IsCancellationRequested)
    {
        attempt++;
        try
        {
            // ... factory setup omitted for brevity (see §5.2)
            _connection = await factory.CreateConnectionAsync(ct);
            _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Set prefetch BEFORE registering the consumer.
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, ct);

            // Verify queue exists without redeclaring it.
            await _channel.QueueDeclarePassiveAsync(_options.QueueName, ct);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;
            await _channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer, ct);

            _logger.LogInformation("Connected. Host={Host} Queue={Queue}", _options.Host, _options.QueueName);
            return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Capped exponential back-off: 2s, 4s, 8s … max 30s.
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
            _logger.LogError(ex, "Connect failed (attempt {Attempt}). Retrying in {Delay}s.", attempt, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }
    }
}
```

> **Jitter note:** When running multiple replicas, add ±20% random jitter to `delay` to prevent a thundering herd of reconnects when the broker recovers. Example: `delay * (0.8 + Random.Shared.NextDouble() * 0.4)`.

### 5.5 Manual Ack / Nack in the Message Handler

```csharp
private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
{
    var correlationId = ea.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString();

    // Structured log scope — every log line within this handler carries CorrelationId + DeliveryTag.
    using var scope = _logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId,
        ["DeliveryTag"]   = ea.DeliveryTag,
    });

    try
    {
        var body = Encoding.UTF8.GetString(ea.Body.Span);
        _logger.LogInformation("Message received. Size={Size}", ea.Body.Length);

        await ProcessMessageAsync(body, correlationId);  // DB upsert + outbox insert

        // Ack only AFTER the database transaction commits successfully.
        await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        _logger.LogInformation("Message acknowledged.");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Processing failed. Routing to dead-letter.");
        // requeue: false → broker routes to DLX if configured on the queue.
        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
    }
}
```

> **When to use `requeue: true`:** Only if you have a bounded retry mechanism backed by the `x-death` header count (see §7.5). An unconditional `requeue: true` on every exception creates a tight hot loop that saturates the broker and starves other consumers.

### 5.6 Idempotency and Duplicate Handling

RabbitMQ guarantees **at-least-once** delivery, not exactly-once [(RabbitMQ reliability guide)](https://www.rabbitmq.com/docs/reliability). The connector must handle re-delivered messages safely.

**Strategy:**

1. Extract a stable idempotency key: prefer `ea.BasicProperties.MessageId`, or fall back to a deterministic hash of the business identifier in the body.
2. Use an **upsert** (`INSERT … ON CONFLICT DO NOTHING` in PostgreSQL, or `MERGE` in SQL Server) keyed on the idempotency key.
3. Write the domain record and outbox row in the **same database transaction**. Ack only after `SaveChangesAsync()` commits.

```csharp
private async Task ProcessMessageAsync(string body, string correlationId)
{
    // 1. Extract idempotency key.
    var message       = JsonSerializer.Deserialize<InboundMessage>(body)!;
    var idempotencyKey = message.EventId;  // stable business key from message body

    // 2. Upsert — safe to re-execute on re-delivery.
    await _db.Messages.UpsertAsync(idempotencyKey, body, correlationId);

    // 3. Outbox row for MQTT publish — same transaction.
    await _db.Outbox.InsertAsync(idempotencyKey, topic: "connector/events", payload: body);

    // 4. Commit once; ack follows in the caller.
    await _db.SaveChangesAsync();
}
```

### 5.7 Configuration: No Hard-Coded Credentials

Sensitive values must never appear in source code. Supply them via environment variables bound to `RabbitMqOptions`:

```json
// appsettings.json — safe defaults for local development only
{
  "RabbitMq": {
    "Host":        "localhost",
    "Port":        5672,
    "VirtualHost": "/",
    "Username":    "guest",
    "Password":    "guest",
    "QueueName":   "connector.inbound"
  }
}
```

In Azure Container Apps, override `Username` and `Password` via [secrets bound to environment variables](https://learn.microsoft.com/azure/container-apps/manage-secrets) referencing Azure Key Vault. The `IOptions<RabbitMqOptions>` binding picks them up automatically.

---

## 6. Best Practices and Anti-Patterns (.NET C#)

### Best Practices

| Practice | Why | Source |
|----------|-----|--------|
| One long-lived `IConnection` per process | Connections are expensive (TCP + TLS). Avoid connection-per-message patterns. | [RabbitMQ connections](https://www.rabbitmq.com/docs/connections) |
| One `IChannel` per logical consumer | Channels are not thread-safe; sharing one across concurrent async callbacks corrupts state. | [RabbitMQ channels](https://www.rabbitmq.com/docs/channels) |
| Use `AsyncEventingBasicConsumer` | The only consumer type that correctly integrates with `async/await` handlers in `RabbitMQ.Client` 7.x. | [.NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide) |
| Call `BasicQosAsync` before `BasicConsumeAsync` | Prefetch limit must be set before the broker begins pushing messages. | [Consumer prefetch](https://www.rabbitmq.com/docs/consumer-prefetch) |
| Always ack or nack every message | Unacknowledged messages consume prefetch slots and eventually block all delivery on the channel. | [Consumer acknowledgements](https://www.rabbitmq.com/docs/confirms) |
| Use `autoAck: false` | Guarantees at-least-once; ack only after successful persistence. | [Acknowledgement modes](https://www.rabbitmq.com/docs/confirms#acknowledgement-modes) |
| Durable queues + `DeliveryMode = 2` (persistent) | Messages survive broker restarts; both must be true for full durability. | [Queue durability](https://www.rabbitmq.com/docs/queues#durability) |
| Prefer Quorum queues | Raft-based replication; classic mirroring is deprecated in RabbitMQ 3.12+. | [Quorum queues](https://www.rabbitmq.com/docs/quorum-queues) |
| Configure a DLX on the inbound queue | Prevents poison messages from blocking progress indefinitely. | [DLX docs](https://www.rabbitmq.com/docs/dlx) |
| Structured logging with `CorrelationId` | Enables end-to-end trace from consume → persist → publish stages. | Project observability baseline |
| TLS on port 5671 | Protects credentials and payload from interception. | [RabbitMQ TLS guide](https://www.rabbitmq.com/docs/ssl) |
| Dispose `IChannel` and `IConnection` on shutdown | Releases TCP connections and broker-side resources cleanly. | [.NET client guide — cleanup](https://www.rabbitmq.com/client-libraries/dotnet-api-guide) |

### Anti-Patterns

| Anti-pattern | Risk | Correct approach |
|-------------|------|-----------------|
| Sharing one `IChannel` across multiple threads | Channel state corruption; random delivery errors or silent connection drops. | One channel per consumer goroutine/async loop. |
| `autoAck: true` in production | Message permanently lost on any processing failure. | Always `autoAck: false`. |
| Blocking async handlers with `.Result` or `.Wait()` | Deadlocks in `IHostedService` context; thread pool starvation. | Use `await` consistently in the entire call chain. |
| Unconditional `requeue: true` on every exception | Tight hot-loop — saturates CPU and starves other consumers from the same queue. | Track `x-death` count; nack to DLX after N retries. |
| Declaring queues from consumer code on every connect | Race conditions with broker startup; declaration failures if parameters differ. | Use `QueueDeclarePassiveAsync` to verify existence; declare via infrastructure tooling. |
| Acking inside a `catch` block | Silently discards processing failures; messages are permanently lost. | Only ack on confirmed success path. |
| New connection per message | Exhausts file descriptors and TCP ports; TLS overhead degrades throughput. | One long-lived `IConnection` per process. |
| Not disposing `IConnection` / `IChannel` | Leaks TCP connections; broker accumulates zombie channels over time. | Override `Dispose()` on the `BackgroundService` worker. |

---

## 7. Reliability and Failure-Mode Analysis

### 7.1 Message Loss Scenarios

| Scenario | Likelihood | Impact | Mitigation |
|----------|-----------|--------|-----------|
| Consumer crashes after delivery but before ack | Medium (replica restart, OOM kill) | None — broker requeues all unacked messages on connection close | `autoAck: false`; handle re-deliveries with idempotency key |
| Non-durable queue or transient message on broker restart | Low if configured correctly | Message permanently lost | Durable quorum queues + `DeliveryMode = 2` |
| Consumer acks before DB transaction commits | Low (coding error) | Permanent data loss if DB write fails | Ack only after `SaveChangesAsync()` returns without exception |
| Network partition between consumer and broker | Low-Medium | Unacked messages held; redelivered on reconnect | Reconnect with back-off; idempotency absorbs duplicates |

### 7.2 Duplicate Delivery

RabbitMQ guarantees **at-least-once** delivery, not exactly-once [(RabbitMQ reliability guide)](https://www.rabbitmq.com/docs/reliability). Duplicates occur on:

- Consumer reconnect after a crash without having acked in-flight messages.
- Nack with `requeue: true`.
- Broker failover and re-delivery of in-flight messages to a new consumer.

**Mitigation:** idempotent upsert keyed on `MessageId` / stable business key (see §5.6).

### 7.3 Consumer Crash and Restart Behavior

When a Container Apps replica restarts:

1. The TCP connection is closed. The broker transitions all unacked messages back to the `Ready` state.
2. Another live replica (or the restarted one after reconnect) receives those messages again.
3. The idempotency upsert at the DB layer makes reprocessing safe — no duplicate records.

`BackgroundService.ExecuteAsync` calls `ConnectWithRetryAsync` at startup with capped exponential back-off, so temporary broker unavailability is handled without manual intervention.

### 7.4 Backpressure and Queue Lag

If the consumer processes messages more slowly than the producer publishes them, queue depth grows. Risks:

- **Memory pressure on the broker:** messages spill to disk in lazy mode, increasing redelivery latency.
- **Consumer latency increase:** head-of-line blocking if prefetch is too high.
- **TTL-driven misrouting to DLX:** if `x-message-ttl` is set and lag exceeds the TTL, messages expire before processing.

Mitigations:

- Monitor queue depth; alert when lag exceeds threshold (e.g., > 10,000 messages or > 5 minutes of peak throughput).
- Scale out Container Apps replicas with a [KEDA scale rule triggered on RabbitMQ queue depth](https://learn.microsoft.com/azure/container-apps/scale-app).
- Tune prefetch after benchmarking actual processing time per message.

### 7.5 Poison Messages

A **poison message** is one that always causes the consumer to throw an unhandled exception. Without a guard, it creates an infinite nack-requeue-redeliver loop, blocking progress for all other messages.

**Detection:** each time a message is routed through a DLX it accumulates an entry in the `x-death` header array [(RabbitMQ DLX — x-death header)](https://www.rabbitmq.com/docs/dlx#effects-on-messages). Read the count to enforce a retry limit:

```csharp
private static int GetDeathCount(BasicDeliverEventArgs ea)
{
    if (ea.BasicProperties.Headers is null) return 0;
    if (!ea.BasicProperties.Headers.TryGetValue("x-death", out var raw)) return 0;
    return raw is List<object> deaths ? deaths.Count : 0;
}

// In OnMessageReceivedAsync, before processing:
if (GetDeathCount(ea) >= 3)
{
    _logger.LogError("Poison message detected after 3 retries. Routing to dead-letter. DeliveryTag={Tag}", ea.DeliveryTag);
    await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
    return;
}
```

### 7.6 Channel-Level Errors

Certain AMQP protocol errors — publishing to a non-existent exchange, declaring a queue with incompatible parameters — cause the broker to **close the channel** while keeping the TCP connection open [(RabbitMQ channels — error handling)](https://www.rabbitmq.com/docs/channels#error-handling).

Subscribe to `IChannel.ChannelShutdownAsync` to detect this:

```csharp
_channel.ChannelShutdownAsync += async (_, args) =>
{
    if (args.Initiator != ShutdownInitiator.Application)
        _logger.LogWarning("Channel closed unexpectedly: {Reason}", args.ReplyText);
    await Task.CompletedTask;
};
```

The keep-alive loop in `ExecuteAsync` detects `_connection.IsOpen == false` and triggers reconnect. A channel-only close (connection still open) also requires channel recreation; hook `ChannelShutdownAsync` to trigger that path.

### 7.7 Connection-Level Failures

When the TCP connection drops, `IConnection.IsOpen` becomes `false`. The keep-alive loop polls every 5 seconds and calls `ConnectWithRetryAsync` to re-establish the connection and re-register the consumer.

### 7.8 Saturation Risk Summary

| Resource | Saturation signal | Recommended alert threshold |
|----------|------------------|-----------------------------|
| Queue depth | Messages accumulate faster than consumed | > 10,000 messages or > 5 min of peak throughput |
| Prefetch slots | All `prefetchCount` slots filled; no new deliveries | Sustained 100% prefetch utilisation |
| DB connection pool | `DbContext` wait time increasing | Connection acquisition > 1 s |
| Container CPU / memory | Throttling or OOM eviction | > 80% sustained for > 5 min |

---

## 8. Tradeoffs

### 8.1 Prefetch Count: Low vs High

| | Low (1–5) | High (50–200) |
|--|-----------|--------------|
| **Throughput** | Lower — round-trip ack per few messages | Higher — pipeline fills network buffer |
| **Latency per message** | Lower — fewer messages queued per replica | Higher head-of-line risk if one message is slow |
| **Fairness across replicas** | Very fair | Can cause work imbalance when one replica is slower |
| **In-flight memory** | Minimal | More messages held in memory per replica |
| **Recommendation** | Good default for slow processing (DB write + MQTT publish) | Better for high-volume lightweight processing |

**Starting point for this project:** prefetch = `10` (current implementation). Benchmark under realistic load before changing.

### 8.2 Manual Ack vs Auto-Ack

| | `autoAck: false` (manual) | `autoAck: true` |
|--|---------------------------|----------------|
| **Delivery guarantee** | At-least-once | At-most-once |
| **Complexity** | Developer must ack or nack every message | Simpler — no ack code |
| **Data safety on crash** | High — unacked messages requeued | Low — message permanently lost on crash |
| **Recommendation** | ✅ Mandatory for this project | ❌ Unacceptable for reliability targets |

### 8.3 Classic Queue vs Quorum Queue

| | Classic queue | Quorum queue |
|--|---------------|--------------|
| **Data safety** | Requires explicit mirror policy; unsafe without it | Raft consensus; majority-write durability by default |
| **Operations** | Mirror policies must be set and validated | No extra policy needed |
| **Throughput** | Higher peak at small scale | Slightly lower; flatter under load |
| **Future support** | Mirroring deprecated in 3.12+ [(notice)](https://www.rabbitmq.com/blog/2021/08/21/4.0-deprecation-announcements) | Actively maintained; preferred path |
| **Recommendation** | ❌ Avoid for new workloads | ✅ Default for production |

### 8.4 Raw `RabbitMQ.Client` vs Abstraction Libraries

`RabbitMQ.Client` 7.x does **not** auto-reconnect on connection drop [(RabbitMQ.Client README)](https://github.com/rabbitmq/rabbitmq-dotnet-client). The application owns reconnect logic.

Community libraries such as **MassTransit** or **EasyNetQ** abstract reconnect, topology declaration, serialization, and retry — at the cost of learning the library's conventions and losing direct visibility into AMQP behavior.

| | Raw `RabbitMQ.Client` | MassTransit / EasyNetQ |
|--|----------------------|-----------------------|
| **Visibility** | Full — every AMQP call is explicit | Abstracted — harder to debug low-level issues |
| **Reconnect** | Manual (as in this project) | Built-in automatic reconnect |
| **Learning curve** | Lower initially (closer to AMQP spec) | Higher — library-specific concepts |
| **Flexibility** | Maximum | Constrained by library conventions |
| **Recommendation for this project** | ✅ Use for POC — explicit, debuggable, no extra dependency | Consider if topology/serialization complexity grows |

---

## 9. Recommendation

For the HA Connector project at its current concept-to-POC stage on Azure Container Apps:

1. **Keep the raw `RabbitMQ.Client` 7.x approach** with `AsyncEventingBasicConsumer` and `autoAck: false`. It is transparent and directly debuggable during fault-injection experiments. *(Judgment — based on the explicit failure-handling requirement stated in the project goals.)*

2. **Confirm with the broker operator that `connector.inbound` uses a quorum queue.** Classic queues without mirroring are unsafe at the 99.99 availability target. *(Judgment.)*

3. **Configure a DLX** on `connector.inbound` before running integration tests. Without it, any unhandled exception will route messages back to the front of the queue indefinitely.

4. **Add the `x-death` poison-message guard** (§7.5) before the first load test. A single malformed test message can block the entire consumer loop.

5. **Keep prefetch at 10** for the POC. Benchmark under realistic synthetic load and tune from evidence, not speculation.

6. **Add jitter to the reconnect back-off** in `ConnectWithRetryAsync` when running three or more replicas to prevent a thundering herd hitting the broker simultaneously.

7. **Never use `autoAck: true`.** The project already has this correct in [`RabbitMqConsumerWorker.cs`](../src/Connector/RabbitMqConsumerWorker.cs).

8. **Inject credentials via Container Apps secrets** referencing Azure Key Vault. The `IOptions<RabbitMqOptions>` binding already reads from configuration; override `Username` and `Password` only at runtime via environment variables [(Container Apps secret management)](https://learn.microsoft.com/azure/container-apps/manage-secrets).

---

## 10. Validation Plan

### 10.1 Unit Tests (xUnit)

Extend the existing [`RabbitMqConsumerWorkerTests.cs`](../src/Connector.Tests/RabbitMqConsumerWorkerTests.cs):

```csharp
[Fact]
public void DefaultOptions_HaveExpectedValues()
{
    var opts = new RabbitMqOptions();
    Assert.Equal("localhost",          opts.Host);
    Assert.Equal(5672,                 opts.Port);
    Assert.Equal("/",                  opts.VirtualHost);
    Assert.Equal("connector.inbound",  opts.QueueName);
}

[Theory]
[InlineData(5671, true)]
[InlineData(5672, false)]
public void SslOption_IsEnabled_BasedOnPort(int port, bool expected)
{
    Assert.Equal(expected, port == 5671);
}

[Fact]
public void GetDeathCount_ReturnsZero_WhenHeadersNull()
{
    // Simulate ea.BasicProperties.Headers == null
    // Assert GetDeathCount returns 0 without throwing.
}

[Fact]
public async Task Worker_StopsGracefully_WhenCancellationFires()
{
    var opts = new RabbitMqOptions { Host = "invalid.local", Port = 5672 };
    using var worker = CreateWorker(opts);
    using var cts    = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    await worker.StartAsync(cts.Token);
    await Task.Delay(400);
    await worker.StopAsync(CancellationToken.None);  // must not throw
}
```

### 10.2 Integration Tests with Testcontainers

Add package `Testcontainers.RabbitMq` and spin up a real broker for integration tests [(Testcontainers RabbitMQ module)](https://dotnet.testcontainers.org/modules/rabbitmq/):

```csharp
public class RabbitMqConsumerIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _broker = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management")
        .Build();

    public Task InitializeAsync() => _broker.StartAsync();
    public Task DisposeAsync()    => _broker.DisposeAsync().AsTask();

    [Fact]
    public async Task Consumer_AcksMessage_AfterSuccessfulProcessing()
    {
        // 1. Publish one test message via RabbitMQ.Client using container connection string.
        // 2. Start the worker pointing at the container.
        // 3. Poll until queue depth == 0 (or timeout).
        // 4. Assert dead-letter queue depth == 0.
    }

    [Fact]
    public async Task Consumer_RoutesToDlx_WhenProcessingThrows()
    {
        // 1. Publish a message that triggers simulated processing failure.
        // 2. Assert main queue depth == 0 and dead-letter queue depth == 1 after max retries.
    }
}
```

### 10.3 Fault-Injection Scenarios

| Scenario | How to inject | Expected behaviour | Pass criteria |
|----------|--------------|-------------------|--------------|
| Broker TCP disconnect during consume | Stop the RabbitMQ container mid-test | Worker logs warning; reconnects with back-off; resumes consuming on restart | No message loss; queue depth returns to 0 |
| Poison message (handler always throws) | Publish intentionally malformed JSON | After 3 retries (x-death count), message routed to DLX | DLX depth = 1; main queue depth = 0 |
| DB unavailable during write | Stop the database container | Worker logs error, nacks; messages remain in broker until DB recovers | 0 acks issued; queue depth stable |
| Container replica restart (scale to 0 then 1) | ACA scale-in / scale-out during active consume | In-flight unacked messages redelivered; idempotency prevents duplicate DB records | DB record count == published message count |
| Multiple replicas competing (3 replicas, burst of 100 messages) | Scale to 3 replicas; publish burst | Each message processed exactly once (idempotency deduplication) | DB record count == 100; 0 duplicates |

### 10.4 Observability Validation Checklist

- [ ] Every consumed message emits a structured log entry containing `CorrelationId` and `DeliveryTag`.
- [ ] Ack and nack outcomes are logged at `Information` and `Error` levels respectively.
- [ ] Dead-letter routing events are logged with the failure reason and retry count.
- [ ] Reconnect attempts are logged with attempt number and back-off delay.
- [ ] Queue lag metric is emitted (OpenTelemetry counter or Azure Monitor custom metric).
- [ ] Alerts are defined for queue lag > threshold and dead-letter rate > threshold.

---

## 11. Open Questions

| # | Question | Impact if unresolved | Owner |
|---|----------|---------------------|-------|
| Q1 | Are quorum queues already configured on the production broker for `connector.inbound`? | Data safety on broker node failure; unsafe without it | Broker operator |
| Q2 | Do upstream producers set `MessageId` or include a stable business key in the body? | Idempotency key strategy; fallback hash may be needed | Integration team |
| Q3 | What is the agreed maximum retry count before routing to DLX (e.g., 3, 5)? | Poison message handling threshold | Architect / product |
| Q4 | What is the expected peak message rate (messages/second)? | Prefetch count, replica count, and DB connection pool sizing | Product / capacity planning |
| Q5 | Is the RabbitMQ broker on the same Azure VNet as the Container Apps environment, or external via internet? | TLS requirements, latency budget, and network routing design | Infrastructure |
| Q6 | Are there compliance requirements limiting retention of message payloads in the DLX queue (e.g., PII, GDPR)? | DLX TTL policy and archiving / purge strategy | Compliance / legal |
| Q7 | Do upstream producers set `DeliveryMode = 2` (persistent)? Non-persistent messages are lost on broker restart even with durable queues. | End-to-end data durability | Integration team |

---

## 12. Sources

> Source links appear inline next to each factual claim throughout the document. This table provides a consolidated reference list.

| # | Source | URL |
|---|--------|-----|
| 1 | RabbitMQ Protocol Guide (AMQP 0-9-1) | https://www.rabbitmq.com/docs/protocol |
| 2 | RabbitMQ Clustering Guide | https://www.rabbitmq.com/docs/clustering |
| 3 | RabbitMQ Virtual Hosts | https://www.rabbitmq.com/docs/vhosts |
| 4 | RabbitMQ Connections Guide | https://www.rabbitmq.com/docs/connections |
| 5 | RabbitMQ Channels Guide | https://www.rabbitmq.com/docs/channels |
| 6 | RabbitMQ Channels — Error Handling | https://www.rabbitmq.com/docs/channels#error-handling |
| 7 | RabbitMQ Exchanges Guide | https://www.rabbitmq.com/docs/exchanges |
| 8 | RabbitMQ Queues Guide | https://www.rabbitmq.com/docs/queues |
| 9 | RabbitMQ Queue Durability | https://www.rabbitmq.com/docs/queues#durability |
| 10 | RabbitMQ Quorum Queues | https://www.rabbitmq.com/docs/quorum-queues |
| 11 | Classic Mirrored Queue Deprecation (3.12+) | https://www.rabbitmq.com/blog/2021/08/21/4.0-deprecation-announcements |
| 12 | RabbitMQ Bindings | https://www.rabbitmq.com/docs/bindings |
| 13 | AMQP 0-9-1 Model — Message Properties | https://www.rabbitmq.com/tutorials/amqp-concepts#message-properties |
| 14 | RabbitMQ Consumer Acknowledgements | https://www.rabbitmq.com/docs/confirms |
| 15 | Acknowledgement Modes | https://www.rabbitmq.com/docs/confirms#acknowledgement-modes |
| 16 | RabbitMQ Consumer Prefetch (BasicQos) | https://www.rabbitmq.com/docs/consumer-prefetch |
| 17 | RabbitMQ Dead-Letter Exchanges | https://www.rabbitmq.com/docs/dlx |
| 18 | DLX x-death Header | https://www.rabbitmq.com/docs/dlx#effects-on-messages |
| 19 | RabbitMQ Reliability Guide | https://www.rabbitmq.com/docs/reliability |
| 20 | RabbitMQ TLS Guide | https://www.rabbitmq.com/docs/ssl |
| 21 | RabbitMQ .NET Client API Guide | https://www.rabbitmq.com/client-libraries/dotnet-api-guide |
| 22 | RabbitMQ.Client 7.0 Release Notes | https://github.com/rabbitmq/rabbitmq-dotnet-client/releases/tag/v7.0.0 |
| 23 | RabbitMQ.Client GitHub README | https://github.com/rabbitmq/rabbitmq-dotnet-client |
| 24 | Microsoft Docs — BackgroundService | https://learn.microsoft.com/dotnet/core/extensions/background-service |
| 25 | Azure Container Apps Overview | https://learn.microsoft.com/azure/container-apps/overview |
| 26 | Azure Container Apps — Secret Management | https://learn.microsoft.com/azure/container-apps/manage-secrets |
| 27 | Azure Container Apps — Scale Rules (KEDA) | https://learn.microsoft.com/azure/container-apps/scale-app |
| 28 | Testcontainers for .NET — RabbitMQ Module | https://dotnet.testcontainers.org/modules/rabbitmq/ |
