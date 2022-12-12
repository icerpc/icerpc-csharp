// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by LogServerAddressFinderDecorator.</summary>
internal static partial class ServerAddressFinderLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventId.FindFailed,
        EventName = nameof(LocationEventId.FindFailed),
        Level = LogLevel.Trace,
        Message = "Failed to find {LocationKind} '{Location}'")]
    internal static partial void LogFindFailed(
        this ILogger logger,
        string locationKind,
        Location location);

    [LoggerMessage(
        EventId = (int)LocationEventId.Found,
        EventName = nameof(LocationEventId.Found),
        Level = LogLevel.Trace,
        Message = "Found {LocationKind} '{Location}' = '{ServiceAddress}'")]
    internal static partial void LogFound(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);
}
