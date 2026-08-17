# kart-shipping-service — messaging contract (human-readable index)

Nothing in this file is hand-maintained business logic - it is a human-readable index over
`message-bus-manifest.json`, the single source of truth. Regenerate this doc from the manifest if
the two ever drift; never edit topology here first.

## Owned exchanges

| Exchange | Type | Purpose |
|---|---|---|
| `shipping.exchange` | topic, durable | Publishes `ShipmentDispatched`, `ShipmentCreationFailed` |
| `shipping.dlx` | topic, durable | Dead-letter exchange for this service's own inbound consumption queue |

## External exchanges bound (not owned)

| Exchange | Owner | Routing key bound |
|---|---|---|
| `order.exchange` | kart-order-service | `order.order.confirmed` → `shipping.order-events.queue` |

## Published events

| Event | Routing key | Consumers (per event-contract.md) |
|---|---|---|
| `ShipmentDispatched` | `shipping.shipment.dispatched` | Order, Delivery Tracking, Analytics, Notification |
| `ShipmentCreationFailed` | `shipping.shipment.creation-failed` | Order, Analytics, Notification |

## Consumed events

| Event | Source | Queue | Retry ladder | DLQ |
|---|---|---|---|---|
| `OrderConfirmed` | kart-order-service | `shipping.order-events.queue` | 1 tier: 30s | `shipping.order-events.dlq` |

## Internal-only (never on RabbitMQ)

`CarrierCallRequested` is written to `shipment_outbox` to drive the carrier-call worker and the
Mongo read-model projector, but is never relayed to RabbitMQ - it has no external consumer.
