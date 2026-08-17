using Kart.Shipping.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kart.Shipping.Infrastructure.Persistence.Converters;

/// <summary>One generic EF Core value converter for every `ITypedEntityId&lt;TId&gt;` - copied verbatim from kart-identity-service's own converter. The captured-delegate indirection dodges CS8927 (static abstract member used in an expression tree).</summary>
internal static class TypedIdValueConverters
{
    public static ValueConverter<TId, Guid> For<TId>() where TId : struct, ITypedEntityId<TId>
    {
        Func<Guid, TId> fromGuid = TId.From;
        return new ValueConverter<TId, Guid>(id => id.Value, value => fromGuid(value));
    }
}
