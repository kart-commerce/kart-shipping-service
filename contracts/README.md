# contracts/

This directory is the synced, implementation-final copy of the design package approved at
`kart-platform/docs/services/kart-shipping-service/` (requirement-spec, architecture, ddd-model,
design-decisions, edge-cases, database-design, event-contract, api-contract.yaml,
message-bus-manifest.json, tickets). It is the single source of truth this service builds and
tests against - loaded at runtime (`message-bus-manifest.json` via `MessageBusManifestLoader`,
copied into the Api build output) and vendored into `ContractTests` (`api-contract.yaml`).

## Deviations from the platform-approved docs (intentional, user-directed)

1. **CQRS MongoDB read side.** `database-design.md` explicitly concludes Shipping does **not**
   need a read model - "every read is a single indexed point read... keyed on order_id." The user
   explicitly requested a sharded-in-production MongoDB read side with a denormalized read
   collection and full CQRS sync anyway - the same override already applied to
   `kart-payment-service` (see its own `contracts/README.md`). This build adds:
   - `shipment_read` MongoDB collection (`Infrastructure/Persistence/ReadModel/`), `_id = shipmentId`,
     sharded on `{_id: "hashed"}` (same choice as `kart-wishlist-service` - even distribution,
     direct-hit for `GetShipment`; `ListShipments`' `orderId`/`status`/`carrier` filters fan out
     across shards, an accepted tradeoff at this service's modest, order-confirmation-tied volume).
   - Sync mechanism is **an in-process poller reading `shipment_outbox` directly**
     (`ReadModelProjectionHostedService`), not a RabbitMQ self-consumption queue like
     `kart-payment-service`/`kart-offer-service` use. Reason: the internal `CarrierCallRequested`
     marker (needed so `Pending` shipments are visible to `ListShipments` for ops triage, not just
     the two externally-published terminal events) is never published to RabbitMQ at all - a
     self-consumer bound to `shipping.exchange` could never see it. The poller reads every
     `shipment_outbox` row (published or not) ordered by a new `outbox_seq` column, so the same
     "read model is always rebuildable from the write model + event log, never written to outside
     a projection" rule still holds.
   - `GetShipment`/`ListShipments` (`GET /v1/shipments{, /{id}}`) read from this Mongo collection,
     not PostgreSQL. `PostgreSQL` remains the sole write-side source of truth in every other respect.

2. **Carrier integration is a self-contained simulator, not a real provider.** No real carrier
   account/API credentials exist in this environment, and `edge-cases.md`/`design-decisions.md`
   defer the exact carrier roster to a future procurement decision. `SimulatedPrimaryCarrierClient`/
   `SimulatedSecondaryCarrierClient` (`Infrastructure/Carriers/`) implement `ICarrierClient`
   deterministically (a small set of documented postal-code sentinels select success / reject /
   timeout) so the full circuit-breaker/fallback/failure flow is exercisable end-to-end without a
   live vendor integration. A real carrier SDK (Shippo/EasyPost/etc.) can be swapped in later
   behind the same interface with zero change to Application/Domain.

3. **`message-bus-manifest.json` here uses the schema `Kart.Shared.Messaging`'s `MessageBusManifest`
   actually parses** (`exchanges[]`/`externalExchanges[]`/`publishedEvents[]`/`queues[]`/
   `deadLetterQueues[]`), not the platform draft's older `{exchange, dlx, retry, dlqs}` shape - the
   same correction `kart-payment-service`'s own manifest already documents making. Business content
   (exchange names, the one inbound queue/binding/retry tier, both published events' routing keys)
   is unchanged from the platform draft.

4. **Two schema additions beyond `database-design.md`, both justified by requirements the docs
   under-specify a mechanism for:**
   - `shipment_outbox.outbox_seq BIGSERIAL` - a monotonic sequence so the Mongo projector's upserts
     are race-free (last-writer-wins by sequence number, not wall-clock arrival) even if two
     horizontally-scaled worker instances claim different rows for the same shipment concurrently.
   - `shipment_idempotency_keys` table - implements the `Idempotency-Key` contract on
     `POST /v1/shipments` precisely (replay identical response on same key+body, `422` on same key
     + a materially different body), which `api-contract.yaml` requires but `database-design.md`
     doesn't specify storage for.

5. **A real `IAuditLogWriter` is wired** (`EfCoreAuditLogWriter`, backed by a new `audit_log`
   table), not `Kart.Shared.Auditing`'s default `NullAuditLogWriter` - no other service on the
   platform has completed this contract with a real sink yet. Row-level `created_by`/`updated_by`
   stamping is also implemented locally (a `SaveChangesInterceptor`) since `Kart.Shared.Auditing`
   does not yet ship one, though `ddd-model.md`'s audit-actor invariant assumes it does.

Everything else in this directory matches the platform-approved design package exactly.
