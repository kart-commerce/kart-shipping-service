namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>
/// Solves primitive obsession for every Guid-backed identity in this service: a raw `Guid`
/// parameter can never be passed to the wrong method by accident once it's wrapped in a
/// `readonly record struct` implementing this interface (CRTP - "curiously recurring template
/// pattern"). Copied verbatim from kart-identity-service's own ITypedEntityId&lt;TSelf&gt; - there is
/// no reusable base for this in Kart.Shared.Domain yet.
/// </summary>
public interface ITypedEntityId<TSelf> where TSelf : struct, ITypedEntityId<TSelf>
{
    Guid Value { get; }

    static abstract TSelf From(Guid value);
}
