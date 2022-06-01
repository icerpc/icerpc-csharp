// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by LogEndpointCacheDecorator.</summary>
internal static partial class EndpointCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventIds.FoundEntry,
        EventName = nameof(LocationEventIds.FoundEntry),
        Level = LogLevel.Trace,
        Message = "found {LocationKind} '{Location}' = '{Proxy}' in cache")]
    internal static partial void LogFoundEntry(
        this ILogger logger,
        string locationKind,
        Location location,
        Proxy proxy);

    [LoggerMessage(
        EventId = (int)LocationEventIds.SetEntry,
        EventName = nameof(LocationEventIds.SetEntry),
        Level = LogLevel.Trace,
        Message = "set {LocationKind} '{Location}' = '{Proxy}' in cache")]
    internal static partial void LogSetEntry(
        this ILogger logger,
        string locationKind,
        Location location,
        Proxy proxy);

    [LoggerMessage(
        EventId = (int)LocationEventIds.RemovedEntry,
        EventName = nameof(LocationEventIds.RemovedEntry),
        Level = LogLevel.Trace,
        Message = "removed {LocationKind} '{Location}' from cache")]
    internal static partial void LogRemovedEntry(
        this ILogger logger,
        string locationKind,
        Location location);
}
