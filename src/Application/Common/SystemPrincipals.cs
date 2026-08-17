namespace Kart.Shipping.Application.Common;

/// <summary>
/// The well-known `system:*` actor sentinels ddd-model.md's audit-actor invariant names -
/// `created_by`/`updated_by` are never NULL. An authenticated ops-principal's own subject claim
/// is used instead when a mutation originates from a real caller (SHIP-6's manual-create endpoint).
/// </summary>
public static class SystemPrincipals
{
    public const string OrderConfirmedConsumer = "system:shipping-order-confirmed-consumer";
    public const string CarrierCallWorker = "system:shipping-carrier-call-worker";
    public const string OutboxRelayPoller = "system:shipping-outbox-relay-poller";
    public const string ReadModelProjector = "system:shipping-read-model-projector";
}
