// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for the locator interceptor.</summary>
    internal static partial class LocatorLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)LocatorEvent.Resolving,
            EventName = nameof(LocatorEvent.Resolving),
            Level = LogLevel.Trace,
            Message = "resolving {LocationKind} {Location}")]
        internal static partial void LogResolving(this ILogger logger, string locationKind, Location location);

        [LoggerMessage(
            EventId = (int)LocatorEvent.Resolved,
            EventName = nameof(LocatorEvent.Resolved),
            Level = LogLevel.Debug,
            Message = "resolved {LocationKind} '{Location}' = '{Proxy}'")]
        internal static partial void LogResolved(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FailedToResolve,
            EventName = nameof(LocatorEvent.FailedToResolve),
            Level = LogLevel.Debug,
            Message = "failed to resolve {LocationKind} '{Location}'")]
        internal static partial void LogFailedToResolve(
            this ILogger logger,
            string locationKind,
            Location location,
            Exception? exception = null);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FoundEntryInCache,
            EventName = nameof(LocatorEvent.FoundEntryInCache),
            Level = LogLevel.Trace,
            Message = "found {LocationKind} '{Location}' = '{Proxy}' in cache")]
        internal static partial void LogFoundEntryInCache(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.SetEntryInCache,
            EventName = nameof(LocatorEvent.SetEntryInCache),
            Level = LogLevel.Trace,
            Message = "set {LocationKind} '{Location}' = '{Proxy}' in cache")]
        internal static partial void LogSetEntryInCache(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.RemovedEntryFromCache,
            EventName = nameof(LocatorEvent.RemovedEntryFromCache),
            Level = LogLevel.Trace,
            Message = "removed {LocationKind} '{Location}' from cache")]
        internal static partial void LogRemovedEntryFromCache(
            this ILogger logger,
            string locationKind,
            Location location);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FindFailed,
            EventName = nameof(LocatorEvent.FindFailed),
            Level = LogLevel.Trace,
            Message = "failed to find {LocationKind} '{Location}'")]
        internal static partial void LogFindFailed(
            this ILogger logger,
            string locationKind,
            Location location);

        [LoggerMessage(
            EventId = (int)LocatorEvent.Found,
            EventName = nameof(LocatorEvent.Found),
            Level = LogLevel.Trace,
            Message = "found {LocationKind} '{Location}' = '{Proxy}'")]
        internal static partial void LogFound(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);
    }
}
