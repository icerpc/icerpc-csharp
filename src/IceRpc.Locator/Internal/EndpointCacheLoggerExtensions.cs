// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by LogEndpointCacheDecorator.</summary>
internal static partial class EndpointCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventId.FoundEntry,
        EventName = nameof(LocationEventId.FoundEntry),
        Level = LogLevel.Trace,
        Message = "found {LocationKind} '{Location}' = '{ServiceAddress}' in cache")]
    internal static partial void LogFoundEntry(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);

    [LoggerMessage(
        EventId = (int)LocationEventId.SetEntry,
        EventName = nameof(LocationEventId.SetEntry),
        Level = LogLevel.Trace,
        Message = "set {LocationKind} '{Location}' = '{ServiceAddress}' in cache")]
    internal static partial void LogSetEntry(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);

    [LoggerMessage(
        EventId = (int)LocationEventId.RemovedEntry,
        EventName = nameof(LocationEventId.RemovedEntry),
        Level = LogLevel.Trace,
        Message = "removed {LocationKind} '{Location}' from cache")]
    internal static partial void LogRemovedEntry(
        this ILogger logger,
        string locationKind,
        Location location);
}
