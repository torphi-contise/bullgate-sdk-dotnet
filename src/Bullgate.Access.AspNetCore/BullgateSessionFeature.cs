namespace Bullgate.Access.AspNetCore;

/// <summary>Exposes an introspected session to the current request, including registration sessions.</summary>
public interface IBullgateSessionFeature
{
    /// <summary>Gets the active Bullgate session.</summary>
    BullgateSession Session { get; }
}

internal sealed record BullgateSessionFeature(BullgateSession Session)
    : IBullgateSessionFeature;
