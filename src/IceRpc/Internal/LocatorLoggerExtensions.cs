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
            EventId = (int)LocatorEvent.ClearAdapterIdCacheEntry,
            EventName = nameof(LocatorEvent.ClearAdapterIdCacheEntry),
            Level = LogLevel.Trace,
            Message = "removed endpoints for adapter ID {AdapterId}, endpoint = {Endpoint}, " +
                      "alt-endpoints = {AltEndpoints}")]
        internal static partial void LogClearAdapterIdCacheEntry(
            this ILogger logger,
            string adapterId,
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ClearWellKnownCacheEntry,
            EventName = nameof(LocatorEvent.ClearWellKnownCacheEntry),
            Level = LogLevel.Trace,
            Message = "removed endpoints for well-known proxy {Identity}, endpoint = {Endpoint}, " +
                      "alt-endpoints = {AltEndpoints}")]
        internal static partial void LogClearWellKnownCacheEntry(
            this ILogger logger,
            Identity identity,
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints);

        [LoggerMessage(
            EventId = (int)LocatorEvent.CouldNotResolveAdapterId,
            EventName = nameof(LocatorEvent.CouldNotResolveAdapterId),
            Level = LogLevel.Debug,
            Message = "could not resolve endpoint(s) for adapter ID = {AdapterId}")]
        internal static partial void LogCouldNotResolveAdapterId(this ILogger logger, string adapterId);

        [LoggerMessage(
            EventId = (int)LocatorEvent.CouldNotResolveWellKnown,
            EventName = nameof(LocatorEvent.CouldNotResolveWellKnown),
            Level = LogLevel.Debug,
            Message = "could not resolve endpoint(s) for well-known proxy = {Identity}")]
        internal static partial void LogCouldNotResolveWellKnown(this ILogger logger, Identity identity);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FoundAdapterIdEntryInCache,
            EventName = nameof(LocatorEvent.FoundAdapterIdEntryInCache),
            Level = LogLevel.Trace,
            Message = "found {KeyKind} {Key}, proxy = {Proxy}")]
        internal static partial void LogFound(this ILogger logger, string keyKind, LocatorClient.Key key, Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FoundAdapterIdEntryInCache,
            EventName = nameof(LocatorEvent.FoundAdapterIdEntryInCache),
            Level = LogLevel.Trace,
            Message = "found entry for adapter ID {AdapterId} in cache, endpoint = {Endpoint}, " +
                      "alt-endpoints = {AltEndpoints}")]
        internal static partial void LogFoundAdapterIdEntryInCache(
            this ILogger logger,
            string adapterId,
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FoundWellKnownEntryInCache,
            EventName = nameof(LocatorEvent.FoundWellKnownEntryInCache),
            Level = LogLevel.Trace,
            Message = "found entry for well-known proxy {Identity} in cache, endpoint = {Endpoint}, " +
                      "alt-endpoints = {AltEndpoints}")]
        internal static partial void LogFoundWellKnownEntryInCache(
            this ILogger logger,
            Identity identity,
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ReceivedInvalidProxyForAdapterId,
            EventName = nameof(LocatorEvent.ReceivedInvalidProxyForAdapterId),
            Level = LogLevel.Debug,
            Message = "locator returned an invalid proxy when resolving adapter ID = {AdapterId}, received = {Proxy}")]
        internal static partial void LogReceivedInvalidProxyForAdapterId(
            this ILogger logger,
            string adapterId,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ReceivedInvalidProxyForWellKnown,
            EventName = nameof(LocatorEvent.ReceivedInvalidProxyForWellKnown),
            Level = LogLevel.Debug,
            Message = "locator returned an invalid proxy when resolving well-known proxy = {Identity}, " +
                      "received = {Proxy}")]
        internal static partial void LogReceivedInvalidProxyForWellKnown(
            this ILogger logger,
            Identity identity,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolveAdapterIdFailure,
            EventName = nameof(LocatorEvent.ResolveAdapterIdFailure),
            Level = LogLevel.Debug,
            Message = "failed to find {KeyKind} {Key}")]
        internal static partial void LogFindFailed(this ILogger logger, string keyKind, LocatorClient.Key key);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolveAdapterIdFailure,
            EventName = nameof(LocatorEvent.ResolveAdapterIdFailure),
            Level = LogLevel.Debug,
            Message = "failed to find {KeyKind} {Key}")]
        internal static partial void LogFindFailedWithException(
            this ILogger logger,
            string keyKind,
            LocatorClient.Key key,
            Exception exception);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolveAdapterIdFailure,
            EventName = nameof(LocatorEvent.ResolveAdapterIdFailure),
            Level = LogLevel.Debug,
            Message = "failure when resolving adapter ID {AdapterId}")]
        internal static partial void LogResolveAdapterIdFailure(
            this ILogger logger,
            string adapterId,
            Exception exception);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolveWellKnownFailure,
            EventName = nameof(LocatorEvent.ResolveWellKnownFailure),
            Level = LogLevel.Debug,
            Message = "failure when resolving well-known proxy {Identity}")]
        internal static partial void LogResolveWellKnownFailure(
            this ILogger logger,
            Identity identity,
            Exception exception);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolvedAdapterId,
            EventName = nameof(LocatorEvent.ResolvedAdapterId),
            Level = LogLevel.Debug,
            Message = "resolved adapter ID using locator, adapter ID = {AdapterId}, endpoint = {Endpoint}, " +
                      "alt-endpoints = {AltEndpoints}")]
        internal static partial void LogResolvedAdapterId(
            this ILogger logger,
            string adapterId,
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolvedWellKnown,
            EventName = nameof(LocatorEvent.ResolvedWellKnown),
            Level = LogLevel.Debug,
            Message = "resolved well-known proxy using locator, well-known proxy = {Identity}, " +
                      "endpoint = {Endpoint}, alt-endpoints = {AltEndpoints}")]
        internal static partial void LogResolvedWellKnown(
            this ILogger logger,
            Identity identity,
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolvingAdapterId,
            EventName = nameof(LocatorEvent.ResolvingAdapterId),
            Level = LogLevel.Debug,
            Message = "resolving adapter ID {AdapterId}")]
        internal static partial void LogResolvingAdapterId(this ILogger logger, string adapterId);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolvingWellKnown,
            EventName = nameof(LocatorEvent.ResolvingWellKnown),
            Level = LogLevel.Debug,
            Message = "resolving well-known proxy {Identity}")]
        internal static partial void LogResolvingWellKnown(this ILogger logger, Identity identity);
    }
}
