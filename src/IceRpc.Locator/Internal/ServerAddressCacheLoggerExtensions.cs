// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by <see cref="LogServerAddressCacheDecorator"/>.
/// </summary>
internal static partial class ServerAddressCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventId.FoundEntry,
        EventName = nameof(LocationEventId.FoundEntry),
        Level = LogLevel.Trace,
        Message = "Found {LocationKind} '{Location}' = '{ServiceAddress}' in cache")]
    internal static partial void LogFoundEntry(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);

    [LoggerMessage(
        EventId = (int)LocationEventId.SetEntry,
        EventName = nameof(LocationEventId.SetEntry),
        Level = LogLevel.Trace,
        Message = "Set {LocationKind} '{Location}' = '{ServiceAddress}' in cache")]
    internal static partial void LogSetEntry(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);

    [LoggerMessage(
        EventId = (int)LocationEventId.RemovedEntry,
        EventName = nameof(LocationEventId.RemovedEntry),
        Level = LogLevel.Trace,
        Message = "Removed {LocationKind} '{Location}' from cache")]
    internal static partial void LogRemovedEntry(
        this ILogger logger,
        string locationKind,
        Location location);
}
