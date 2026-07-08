# Order System — a minimal distributed microservices demo

Three .NET 10 services on **Azure Container Apps**, talking through **Azure Service
Bus** via **NServiceBus**, persisting to **Cosmos DB**. Built as an interview
refresher for distributed-systems concepts.

```
                HTTP POST /orders
                       │
               ┌───────▼────────┐
               │   Orders.Api    │  AutoFaker generates a fake order,
               │ (ASP.NET Core)  │  SENDS a PlaceOrder COMMAND, returns 202
               └───────┬────────┘
                       │  Service Bus QUEUE "orders-worker" (point-to-point)
               ┌───────▼────────┐
               │  Orders.Worker  │  Upserts order into Cosmos (idempotent),
               │  (NServiceBus)  │  PUBLISHES an OrderPlaced EVENT
               └───────┬────────┘
                       │  Service Bus TOPIC (pub/sub)
               ┌───────▼────────┐
               │ Billing.Worker  │  Subscribes to OrderPlaced,
               │  (NServiceBus)  │  writes an invoice to Cosmos
               └────────────────┘
```

## The concepts this demonstrates

- **Command vs Event** — `PlaceOrder : ICommand` is an instruction sent to one
  known endpoint over a queue. `OrderPlaced : IEvent` is a fact published to a
  topic; subscribers are unknown to the publisher. Adding a Shipping service
  tomorrow requires zero changes to existing services.
- **Eventual consistency** — the API returns **202 Accepted**, not 201. At that
  moment the order exists only as a durable message on the broker. The system
  converges to a consistent state asynchronously.
- **At-least-once delivery & idempotency** — Service Bus redelivers on lock
  loss/crash. Handlers are safe to re-run because document IDs are *derived
  from the message* (order id → doc id, `inv-{orderId}` → invoice id) and
  writes are **upserts**: reprocessing converges instead of duplicating.
- **Retries & poison messages** — NServiceBus does immediate + delayed retries,
  then dead-letters to the `error` queue (`SendFailedMessagesTo("error")`),
  so a bad message never blocks the queue or gets silently lost.
- **Cosmos DB partitioning** — both containers use the document id as partition
  key: perfect distribution for point-writes/point-reads (the only access
  pattern here). A real system would choose the partition key around its
  dominant query, e.g. `/customerId` to list a customer's orders.
- **CAP in practice** — the system stays available for writes even if Cosmos or
  a downstream service is briefly down: messages simply queue up (temporal
  decoupling). You trade read-your-writes consistency for that availability.

## Layout

| Project | Role |
|---|---|
| `src/Messages` | Shared message contracts (the only coupling between services) |
| `src/Orders.Api` | HTTP entry point, send-only NServiceBus endpoint, AutoFaker data |
| `src/Orders.Worker` | Handles `PlaceOrder`, writes to Cosmos, publishes `OrderPlaced` |
| `src/Billing.Worker` | Subscribes to `OrderPlaced`, writes invoices to Cosmos |

## Deploy

```bash
az login
./infra/deploy.sh          # provisions everything, builds images in ACR, deploys
curl -si -X POST https://<api-fqdn>/orders
```

Tear down: `az group delete -n order-system-rg --yes --no-wait`

## Production gaps (deliberate, for a 2-hour demo — know these for the interview)

- Connection strings via secrets/env vars → should be **managed identity** + Key Vault
- No **outbox**: the Cosmos write and the Publish are not atomic. A crash between
  them means an order without an invoice until the message is retried
  (NServiceBus's Cosmos persistence provides an outbox for exactly this).
- No observability stack (OpenTelemetry, distributed tracing across the message hops)
- `min-replicas 1` fixed scaling → would use KEDA queue-length autoscaling in ACA
- Installers (`EnableInstallers`) create topology at startup → deployment-time step in prod
