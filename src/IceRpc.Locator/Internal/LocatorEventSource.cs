// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace IceRpc.Locator.Internal;

/// <summary>An <see cref="EventSource" /> implementation used to log locator events.</summary>
internal sealed class LocatorEventSource : EventSource
{
    internal static readonly LocatorEventSource Log = new("IceRpc-Locator");

    /// <summary>Creates a new instance of the <see cref="LocatorEventSource" /> class with the specified name.
    /// </summary>
    /// <param name="eventSourceName">The name to apply to the event source.</param>
    internal LocatorEventSource(string eventSourceName)
        : base(eventSourceName)
    {
    }

    [NonEvent]
    internal void Find(Location location, ServiceAddress? serviceAddress)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            Find(location.Kind, location.ToString(), serviceAddress?.ToString());
        }
    }

    [NonEvent]
    internal void FindCacheEntry(Location location, ServiceAddress? serviceAddress)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            FindCacheEntry(location.Kind, location.ToString(), serviceAddress?.ToString());
        }
    }

    [NonEvent]
    internal void RemoveCacheEntry(Location location)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            RemoveCacheEntry(location.Kind, location.ToString());
        }
    }

    [NonEvent]
    internal void ResolveFailure(Location location, Exception exception)
    {
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ResolveFailure(
                location.Kind,
                location.ToString(),
                exception.GetType().FullName,
                exception.ToString());
        }
    }

    [NonEvent]
    internal void ResolveStart(Location location)
    {
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ResolveStart(location.Kind, location.ToString());
        }
    }

    [NonEvent]
    internal void ResolveStop(Location location, ServiceAddress? serviceAddress)
    {
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ResolveStop(location.Kind, location.ToString(), serviceAddress?.ToString());
        }
    }

    [NonEvent]
    internal void SetCacheEntry(Location location, ServiceAddress serviceAddress)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            SetCacheEntry(location.Kind, location.ToString(), serviceAddress.ToString());
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(1, Level = EventLevel.Verbose)]
    private void Find(string locationKind, string location, string? serviceAddress) =>
        WriteEvent(1, locationKind, location, serviceAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(2, Level = EventLevel.Verbose)]
    private void FindCacheEntry(string locationKind, string location, string? serviceAddress) =>
        WriteEvent(2, locationKind, location, serviceAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(3, Level = EventLevel.Verbose)]
    private void RemoveCacheEntry(string locationKind, string location) =>
        WriteEvent(3, locationKind, location);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(4, Level = EventLevel.Error)]
    private void ResolveFailure(
        string locationKind,
        string location,
        string? exceptionType,
        string exceptionDetails) =>
        WriteEvent(4, locationKind, location, exceptionType, exceptionDetails);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(5, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ResolveStart(string locationKind, string location) =>
        WriteEvent(5, locationKind, location);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(6, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ResolveStop(string locationKind, string location, string? serviceAddress) =>
        WriteEvent(6, locationKind, location, serviceAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(7, Level = EventLevel.Verbose)]
    private void SetCacheEntry(string locationKind, string location, string serviceAddress) =>
        WriteEvent(7, locationKind, location, serviceAddress);
}
