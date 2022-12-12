// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by <see cref="LogLocationResolverDecorator"/>.</summary>
internal static partial class LocatorLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventId.Resolved,
        EventName = nameof(LocationEventId.Resolved),
        Level = LogLevel.Debug,
        Message = "Resolved {LocationKind} '{Location}' = '{ServiceAddress}'")]
    internal static partial void LogResolved(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);

    [LoggerMessage(
        EventId = (int)LocationEventId.FailedToResolve,
        EventName = nameof(LocationEventId.FailedToResolve),
        Level = LogLevel.Debug,
        Message = "Failed to resolve {LocationKind} '{Location}'")]
    internal static partial void LogFailedToResolve(
        this ILogger logger,
        string locationKind,
        Location location,
        Exception? exception = null);
}
