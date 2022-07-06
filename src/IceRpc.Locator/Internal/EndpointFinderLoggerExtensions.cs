// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by LogEndpointFinderDecorator.</summary>
internal static partial class EndpointFinderLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventIds.FindFailed,
        EventName = nameof(LocationEventIds.FindFailed),
        Level = LogLevel.Trace,
        Message = "failed to find {LocationKind} '{Location}'")]
    internal static partial void LogFindFailed(
        this ILogger logger,
        string locationKind,
        Location location);

    [LoggerMessage(
        EventId = (int)LocationEventIds.Found,
        EventName = nameof(LocationEventIds.Found),
        Level = LogLevel.Trace,
        Message = "found {LocationKind} '{Location}' = '{ServiceAddress}'")]
    internal static partial void LogFound(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);
}
