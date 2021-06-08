// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Immutable;

#pragma warning disable SYSLIB1006 // Multiple logging methods are using the same event id

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for the locator interceptor.</summary>
    internal static partial class LocatorLoggerExtensions
    {
        internal static void LogClearCacheEntry(
            this ILogger logger,
            string location,
            string? category,
            Endpoint endpoint,
            ImmutableList<Endpoint> altEndpoints)
        {
            // TODO logg altEndpoints once https://github.com/dotnet/runtime/issues/51965 is fixed
            if (category == null)
            {
                logger.LogClearAdapterCacheEntry(location, endpoint);
            }
            else
            {
                logger.LogClearWellKnownCacheEntry(new Identity(location, category), endpoint);
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.ClearCacheEntry,
            EventName = nameof(LocatorEvent.ClearCacheEntry),
            Level = LogLevel.Trace,
            Message = "removed endpoints for adapter ID {AdapterId}, endpoint = {Endpoint}")]
        internal static partial void LogClearAdapterCacheEntry(
            this ILogger logger,
            string adapterId,
            Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ClearCacheEntry,
            EventName = nameof(LocatorEvent.ClearCacheEntry),
            Level = LogLevel.Trace,
            Message = "removed endpoints for well-known proxy {Identity}, endpoint = {Endpoint}")]
        internal static partial void LogClearWellKnownCacheEntry(
            this ILogger logger,
            Identity identity,
            Endpoint endpoint);

        internal static void LogCouldNotResolveEndpoint(this ILogger logger, string location, string? category)
        {
            if (category == null)
            {
                logger.LogCouldNotResolveAdapterEndpoint(location);
            }
            else
            {
                logger.LogCouldNotResolveWellKnownEndpoint(new Identity(location, category));
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.CouldNotResolveEndpoint,
            EventName = nameof(LocatorEvent.CouldNotResolveEndpoint),
            Level = LogLevel.Trace,
            Message = "could not resolve endpoint(s) for adapter ID = {AdapterId}")]
        internal static partial void LogCouldNotResolveAdapterEndpoint(this ILogger logger, string adapterId);

        [LoggerMessage(
            EventId = (int)LocatorEvent.CouldNotResolveEndpoint,
            EventName = nameof(LocatorEvent.CouldNotResolveEndpoint),
            Level = LogLevel.Trace,
            Message = "could not resolve endpoint(s) for well-known proxy = {Identity}")]
        internal static partial void LogCouldNotResolveWellKnownEndpoint(this ILogger logger, Identity identity);

        internal static void LogFoundEntryInCache(
            this ILogger logger,
            string location,
            string? category,
            Endpoint endpoint,
            ImmutableList<Endpoint> altEndpoints)
        {
            // TODO logg altEndpoints once https://github.com/dotnet/runtime/issues/51965 is fixed
            if (category == null)
            {
                logger.LogFoundAdapterEntryInCache(location, endpoint);
            }
            else
            {
                logger.LogFoundWellKnownEntryInCache(new Identity(location, category), endpoint);
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.FoundEntryInCache,
            EventName = nameof(LocatorEvent.FoundEntryInCache),
            Level = LogLevel.Trace,
            Message = "found entry for adapter ID {AdapterId} in cache, endpoint = {Endpoint}")]
        internal static partial void LogFoundAdapterEntryInCache(
            this ILogger logger,
            string adapterId,
            Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)LocatorEvent.FoundEntryInCache,
            EventName = nameof(LocatorEvent.FoundEntryInCache),
            Level = LogLevel.Trace,
            Message = "found entry for well-known proxy {Identity} in cache, endpoint = {Endpoint}")]
        internal static partial void LogFoundWellKnownEntryInCache(
            this ILogger logger,
            Identity identity,
            Endpoint endpoint);

        internal static void LogReceivedInvalidProxy(
            this ILogger logger,
            string location,
            string? category,
            ServicePrx proxy)
        {
            if (category == null)
            {
                logger.LogReceivedInvalidProxyForAdapter(location, proxy);
            }
            else
            {
                logger.LogReceivedInvalidProxyForWellKnown(new Identity(location, category), proxy);
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.ReceivedInvalidProxy,
            EventName = nameof(LocatorEvent.ReceivedInvalidProxy),
            Level = LogLevel.Debug,
            Message = "locator returned an invalid proxy when resolving adapter ID = {AdapterId}, received = {Proxy}")]
        internal static partial void LogReceivedInvalidProxyForAdapter(this ILogger logger, string adapterId, ServicePrx proxy);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ReceivedInvalidProxy,
            EventName = nameof(LocatorEvent.ReceivedInvalidProxy),
            Level = LogLevel.Debug,
            Message = "locator returned an invalid proxy when resolving well-known proxy = {Identity}, received = {Proxy}")]
        internal static partial void LogReceivedInvalidProxyForWellKnown(this ILogger logger, Identity identity, ServicePrx proxy);

        internal static void LogResolveFailure(
            this ILogger logger,
            string location,
            string? category,
            Exception exception)
        {
            if (category == null)
            {
                logger.LogResolvedAdapterFailure(location, exception);
            }
            else
            {
                logger.LogResolvedWellKnownFailure(new Identity(location, category), exception);
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolveFailure,
            EventName = nameof(LocatorEvent.ResolveFailure),
            Level = LogLevel.Debug,
            Message = "failure when resolving adapter ID {AdapterId}")]
        internal static partial void LogResolvedAdapterFailure(this ILogger logger, string adapterId, Exception exception);

        [LoggerMessage(
            EventId = (int)LocatorEvent.ResolveFailure,
            EventName = nameof(LocatorEvent.ResolveFailure),
            Level = LogLevel.Debug,
            Message = "failure when resolving well-known proxy {Identity}")]
        internal static partial void LogResolvedWellKnownFailure(this ILogger logger, Identity identity, Exception exception);

        internal static void LogResolved(
            this ILogger logger,
            string location,
            string? category,
            Endpoint endpoint,
            ImmutableList<Endpoint> altEndpoints)
        {
            // TODO logg altEndpoints once https://github.com/dotnet/runtime/issues/51965 is fixed
            if (category == null)
            {
                logger.LogResolvedAdapter(location, endpoint);
            }
            else
            {
                logger.LogResolvedWellKnown(new Identity(location, category), endpoint);
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.Resolved,
            EventName = nameof(LocatorEvent.Resolved),
            Level = LogLevel.Debug,
            Message = "resolved adapter ID using locator, adapter ID = {AdapterId}, endpoint = {Endpoint}")]
        internal static partial void LogResolvedAdapter(
            this ILogger logger,
            string adapterId,
            Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)LocatorEvent.Resolved,
            EventName = nameof(LocatorEvent.Resolved),
            Level = LogLevel.Debug,
            Message = "resolved well-known proxy using locator, well-known proxy = {Identity}, " +
                      "endpoint = {Endpoint}")]
        internal static partial void LogResolvedWellKnown(
            this ILogger logger,
            Identity identity,
            Endpoint endpoint);

        internal static void LogResolving(this ILogger logger, string location, string? category)
        {
            if (category == null)
            {
                logger.LogResolvingAdapter(location);
            }
            else
            {
                logger.LogResolvingWellKnown(new Identity(location, category));
            }
        }

        [LoggerMessage(
            EventId = (int)LocatorEvent.Resolving,
            EventName = nameof(LocatorEvent.Resolving),
            Level = LogLevel.Debug,
            Message = "resolving adapter ID {AdapterId}")]
        internal static partial void LogResolvingAdapter(this ILogger logger, string adapterId);

        [LoggerMessage(
            EventId = (int)LocatorEvent.Resolving,
            EventName = nameof(LocatorEvent.Resolving),
            Level = LogLevel.Debug,
            Message = "resolving well-known proxy {Identity}")]
        internal static partial void LogResolvingWellKnown(this ILogger logger, Identity identity);
    }
}
