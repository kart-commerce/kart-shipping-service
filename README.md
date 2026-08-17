# kart-shipping-service

Post-confirmation, fully-async fulfillment step (BRD §2.1 item 16): carrier selection and label
generation for an already-confirmed order. Sole trigger is consuming `OrderConfirmed`
(ADR-0002) — this service has no synchronous inbound endpoint Order calls — and it publishes
`ShipmentDispatched`/`ShipmentCreationFailed` (ADR-0015) once the out-of-band carrier interaction
resolves. One aggregate (`Shipment`, keyed on `order_id`), PostgreSQL as the write-side source of
truth, and (per an explicit, documented deviation from the platform's own design docs — see
`contracts/README.md`) a denormalized, sharded-in-production MongoDB read model for full CQRS.

Design docs: `kart-platform/docs/services/kart-shipping-service/`. Deviations from those docs:
`contracts/README.md`.

## Layout

Clean Architecture + Vertical Slice, mirroring `kart-identity-service`/`kart-payment-service`:

```
src/
├── Api/              # ASP.NET Core minimal API endpoints (SHIP-4/5/6), thin
├── Application/       # Features/<UseCaseName>/ vertical slices (MediatR) + Common/{Behaviours,Interfaces,Models}
├── Domain/             # Shipment aggregate, typed IDs, value objects, enums
└── Infrastructure/    # EF Core (Postgres write side), MongoDB (CQRS read side), RabbitMQ
                        # (manifest-driven outbox relay + consumers), simulated carriers, auditing
tests/
├── UnitTests/          # mirrors Application/Features + Domain + Infrastructure/Carriers
├── IntegrationTests/   # real Postgres/Mongo/RabbitMQ via Testcontainers
└── ContractTests/      # validates live responses against contracts/api-contract.yaml
contracts/              # synced copy of the approved design package + documented deviations
```

## Business flow

1. **SHIP-1**: `OrderConfirmedConsumerHostedService` consumes `OrderConfirmed` → persists a
   `Shipment` (`Pending`) + a `CarrierCallRequested` outbox marker in one transaction. Idempotent:
   a redelivered/duplicate `OrderConfirmed` for an order that already has a shipment is a no-op.
2. **SHIP-2**: `CarrierCallWorkerHostedService` claims `CarrierCallRequested` markers
   (`FOR UPDATE SKIP LOCKED`) and walks the configured `Carriers:Priority` list, each behind its
   own circuit breaker + retry pipeline, resolving to `Dispatched` or `Failed`.
3. **SHIP-3**: `OutboxRelayHostedService` publishes the resolved `ShipmentDispatched`/
   `ShipmentCreationFailed` domain events to RabbitMQ, per `contracts/message-bus-manifest.json`.
4. **Read-model sync**: `ReadModelProjectionHostedService` projects every `shipment_outbox` row
   (including the internal `CarrierCallRequested` marker) into the MongoDB `shipment_read`
   collection, race-free via a monotonic `outbox_seq` guard.
5. **SHIP-4/5/6**: `GET /v1/shipments`, `GET /v1/shipments/{id}` (read from Mongo), and
   `POST /v1/shipments` (ops-only manual creation, idempotency-key protected, reuses SHIP-1's
   exact creation path).

## Running locally

Requires the .NET 8 SDK and Docker (for Postgres/Mongo/RabbitMQ).

```
docker compose up -d postgres mongo rabbitmq
export SHIPPING_DB_CONNECTION_STRING="Host=localhost;Port=5434;Database=kart_shipping_dev;Username=postgres;Password=postgres"
dotnet tool restore
scripts/migrate.sh
dotnet build
dotnet test
```

`src/Api/appsettings.Local.json.example` documents the `GlobalConfig:Path` bootstrap every
service uses for real secrets (connection strings, RabbitMQ credentials) — copy it to
`appsettings.Local.json` (gitignored) and point it at your machine's shared GlobalConfig file, or
just set the equivalent `ConnectionStrings__ShippingDatabase` / `RabbitMq__UserName` /
`RabbitMq__Password` environment variables directly for local dev against the compose stack above.

Carrier integration is a self-contained simulator (no real account needed) — see
`contracts/README.md` deviation #2 for the exact postal-code sentinels that drive every scenario
(success / fallback / both-carriers-reject / both-carriers-timeout) deterministically.
