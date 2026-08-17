using Kart.Shipping.Infrastructure.Persistence.ReadModel.Documents;
using MongoDB.Driver;

namespace Kart.Shipping.Infrastructure.Persistence.ReadModel;

/// <summary>
/// The write path for this service's CQRS read side - called exclusively by
/// <see cref="Messaging.ReadModelProjectionHostedService"/>, never by a request handler. Every
/// apply is a two-step, race-free upsert: (1) unconditionally ensure a document shell exists
/// (never touches an existing document's fields), then (2) a plain conditional update guarded by
/// `LastAppliedSeq &lt; outbox_seq` - never combining `IsUpsert` with a non-identity filter
/// condition, which would otherwise risk a duplicate-key insert attempt if a concurrent writer
/// already advanced the document past this event's own sequence number.
/// </summary>
public sealed class ReadModelProjectionWriter(ShippingReadDbContext context)
{
    public Task ApplyPendingAsync(Guid shipmentId, string orderId, DateTime createdAt, long seq, CancellationToken cancellationToken) =>
        ApplyIfNewerAsync(
            shipmentId,
            seq,
            Builders<ShipmentReadDocument>.Update
                .Set(d => d.OrderId, orderId)
                .Set(d => d.Status, "Pending")
                .Set(d => d.CreatedAt, createdAt),
            cancellationToken);

    public Task ApplyDispatchedAsync(Guid shipmentId, string orderId, string carrier, string trackingId, DateTime dispatchedAt, long seq, CancellationToken cancellationToken) =>
        ApplyIfNewerAsync(
            shipmentId,
            seq,
            Builders<ShipmentReadDocument>.Update
                .Set(d => d.OrderId, orderId)
                .Set(d => d.Status, "Dispatched")
                .Set(d => d.Carrier, carrier)
                .Set(d => d.TrackingId, trackingId)
                .Set(d => d.DispatchedAt, dispatchedAt),
            cancellationToken);

    public Task ApplyFailedAsync(Guid shipmentId, string orderId, string failureReason, DateTime failedAt, long seq, CancellationToken cancellationToken) =>
        ApplyIfNewerAsync(
            shipmentId,
            seq,
            Builders<ShipmentReadDocument>.Update
                .Set(d => d.OrderId, orderId)
                .Set(d => d.Status, "Failed")
                .Set(d => d.FailureReason, failureReason)
                .Set(d => d.FailedAt, failedAt),
            cancellationToken);

    private async Task ApplyIfNewerAsync(Guid shipmentId, long seq, UpdateDefinition<ShipmentReadDocument> fieldUpdate, CancellationToken cancellationToken)
    {
        // Step 1: ensure the document shell exists. A no-op $setOnInsert-only update can never
        // conflict with an existing document's fields.
        await context.Shipments.UpdateOneAsync(
            Builders<ShipmentReadDocument>.Filter.Eq(d => d.Id, shipmentId),
            Builders<ShipmentReadDocument>.Update.SetOnInsert(d => d.Id, shipmentId).SetOnInsert(d => d.LastAppliedSeq, 0L),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

        // Step 2: plain (non-upsert) conditional update - guaranteed to exist after step 1, so a
        // failed `LastAppliedSeq < seq` guard is simply a no-op match-zero-rows, never an upsert
        // attempting to re-insert an id that already exists.
        var filter = Builders<ShipmentReadDocument>.Filter.And(
            Builders<ShipmentReadDocument>.Filter.Eq(d => d.Id, shipmentId),
            Builders<ShipmentReadDocument>.Filter.Lt(d => d.LastAppliedSeq, seq));

        var update = Builders<ShipmentReadDocument>.Update.Combine(fieldUpdate, Builders<ShipmentReadDocument>.Update.Set(d => d.LastAppliedSeq, seq));

        await context.Shipments.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }
}
