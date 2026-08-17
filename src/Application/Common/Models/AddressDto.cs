namespace Kart.Shipping.Application.Common.Models;

/// <summary>api-contract.yaml `Address` schema / `OrderConfirmed`'s `address` payload field.</summary>
public sealed record AddressDto(string Line1, string? Line2, string City, string? State, string PostalCode, string Country);
