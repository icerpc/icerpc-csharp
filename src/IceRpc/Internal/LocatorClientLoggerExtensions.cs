// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Immutable;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging messages in the
    /// "IceRpc.Interop.LocatorClient" category.</summary>
    internal static class LocatorClientLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.LocatorClientBaseEventId;
        private const int ClearCacheEntry = BaseEventId + 0;
        private const int CouldNotResolveEndpoint = BaseEventId + 1;
        private const int FoundEntryInCache = BaseEventId + 2;
        private const int ReceivedInvalidProxy = BaseEventId + 3;
        private const int ResolveFailure = BaseEventId + 4;
        private const int Resolved = BaseEventId + 5;
        private const int Resolving = BaseEventId + 6;

        private static readonly Action<ILogger, string, Endpoint, ImmutableList<Endpoint>, Exception> _clearAdapterCacheEntry =
            LoggerMessage.Define<string, Endpoint, ImmutableList<Endpoint>>(
                LogLevel.Trace,
                new EventId(ClearCacheEntry, nameof(ClearCacheEntry)),
                "removed endpoints for adapter ID {AdapterId}, endpoint = {Endpoint}, alt-endpoint = {AltEndpoints}");
        private static readonly Action<ILogger, Identity, Endpoint, ImmutableList<Endpoint>, Exception> _clearWellKnownCacheEntry =
            LoggerMessage.Define<Identity, Endpoint, ImmutableList<Endpoint>>(
                LogLevel.Trace,
                new EventId(ClearCacheEntry, nameof(ClearCacheEntry)),
                "removed endpoints for well-known proxy {Identity}, endpoint = {Endpoint}, alt-endpoint = {AltEndpoints}");

        private static readonly Action<ILogger, string, Exception> _couldNotResolveAdapterEndpoint =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(CouldNotResolveEndpoint, nameof(CouldNotResolveEndpoint)),
                "could not resolve endpoint(s) for adapter ID = {AdapterId}");

        private static readonly Action<ILogger, Identity, Exception> _couldNotResolveWellKnownEndpoint =
            LoggerMessage.Define<Identity>(
                LogLevel.Debug,
                new EventId(CouldNotResolveEndpoint, nameof(CouldNotResolveEndpoint)),
                "could not resolve endpoint(s) for well-known proxy = {Identity}");

        private static readonly Action<ILogger, string, Endpoint, ImmutableList<Endpoint>, Exception> _foundAdapterEntryInCache =
            LoggerMessage.Define<string, Endpoint, ImmutableList<Endpoint>>(
                LogLevel.Trace,
                new EventId(FoundEntryInCache, nameof(FoundEntryInCache)),
                "found entry for adapter ID {AdapterId} in cache, endpoint = {Endpoint}, alt-endpoint = {AltEndpoints}");

        private static readonly Action<ILogger, Identity, Endpoint, ImmutableList<Endpoint>, Exception> _foundWellKnownEntryInCache =
            LoggerMessage.Define<Identity, Endpoint, ImmutableList<Endpoint>>(
                LogLevel.Trace,
                new EventId(FoundEntryInCache, nameof(FoundEntryInCache)),
                "found entry for well-known proxy {Identity} in cache, endpoint = {Endpoint}, alt-endpoint = {AltEndpoints}");

        private static readonly Action<ILogger, string, ServicePrx, Exception> _receivedInvalidProxyForAdapter =
            LoggerMessage.Define<string, ServicePrx>(
                LogLevel.Debug,
                new EventId(ReceivedInvalidProxy, nameof(ReceivedInvalidProxy)),
                "locator returned an invalid proxy when resolving adapter ID = {AdapterId}, received = {Proxy}");

        private static readonly Action<ILogger, Identity, ServicePrx, Exception> _receivedInvalidProxyForWellKnown =
            LoggerMessage.Define<Identity, ServicePrx>(
                LogLevel.Debug,
                new EventId(ReceivedInvalidProxy, nameof(ReceivedInvalidProxy)),
                "locator returned an invalid proxy when resolving well-known proxy = {Identity}, received = {Proxy}");

        private static readonly Action<ILogger, string, Exception> _resolveAdapterFailure =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(ResolveFailure, nameof(ResolveFailure)), "failure when resolving adapter ID {AdapterId}");

        private static readonly Action<ILogger, Identity, Exception> _resolveWellKnownFailure =
            LoggerMessage.Define<Identity>(
                LogLevel.Debug,
                new EventId(ResolveFailure, nameof(ResolveFailure)),
                "failure when resolving well-known proxy {Identity}");

        private static readonly Action<ILogger, string, Endpoint, ImmutableList<Endpoint>, Exception> _resolvedAdapter =
            LoggerMessage.Define<string, Endpoint, ImmutableList<Endpoint>>(
                LogLevel.Debug,
                new EventId(Resolved, nameof(Resolved)),
                "resolved adapter ID using locator, adapter ID = {AdapterId}, endpoint = {Endpoint}, alt-endpoint = {AltEndpoints}");

        private static readonly Action<ILogger, Identity, Endpoint, ImmutableList<Endpoint>, Exception> _resolvedWellKnown =
            LoggerMessage.Define<Identity, Endpoint, ImmutableList<Endpoint>>(
                LogLevel.Debug,
                new EventId(Resolved, nameof(Resolved)),
                "resolved well-known proxy using locator, well-known proxy = {Identity}, endpoint = {Endpoint}, alt-endpoint = {AltEndpoints}");

        private static readonly Action<ILogger, string, Exception> _resolvingAdapter = LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(Resolving, nameof(Resolving)),
            "resolving adapter ID {AdapterId}");

        private static readonly Action<ILogger, Identity, Exception> _resolvingWellKnown =
            LoggerMessage.Define<Identity>(
                LogLevel.Debug,
                new EventId(Resolving, nameof(Resolving)),
                "resolving well-known proxy {Identity}");

        internal static void LogClearCacheEntry(
            this ILogger logger,
            string location,
            string? category,
            Endpoint endpoint,
            ImmutableList<Endpoint> altEndpoints)
        {
            if (category == null)
            {
                _clearAdapterCacheEntry(logger, location, endpoint, altEndpoints, null!);
            }
            else
            {
                _clearWellKnownCacheEntry(logger, new Identity(location, category), endpoint, altEndpoints, null!);
            }
        }

        internal static void LogCouldNotResolveEndpoint(this ILogger logger, string location, string? category)
        {
            if (category == null)
            {
                _couldNotResolveAdapterEndpoint(logger, location, null!);
            }
            else
            {
                _couldNotResolveWellKnownEndpoint(logger, new Identity(location, category), null!);
            }
        }

        internal static void LogFoundEntryInCache(
            this ILogger logger,
            string location,
            string? category,
            Endpoint endpoint,
            ImmutableList<Endpoint> altEndpoints)
        {
            if (category == null)
            {
                _foundAdapterEntryInCache(logger, location, endpoint, altEndpoints, null!);
            }
            else
            {
                _foundWellKnownEntryInCache(logger, new Identity(location, category), endpoint, altEndpoints, null!);
            }
        }

        internal static void LogReceivedInvalidProxy(
            this ILogger logger,
            string location,
            string? category,
            ServicePrx proxy)
        {
            if (category == null)
            {
                _receivedInvalidProxyForAdapter(logger, location, proxy, null!);
            }
            else
            {
                _receivedInvalidProxyForWellKnown(logger, new Identity(location, category), proxy, null!);
            }
        }

        internal static void LogResolveFailure(
            this ILogger logger,
            string location,
            string? category,
            Exception exception)
        {
            if (category == null)
            {
                _resolveAdapterFailure(logger, location, exception);
            }
            else
            {
                _resolveWellKnownFailure(logger, new Identity(location, category), exception);
            }
        }

        internal static void LogResolved(
            this ILogger logger,
            string location,
            string? category,
            Endpoint endpoint,
            ImmutableList<Endpoint> altEndpoints)
        {
            if (category == null)
            {
                _resolvedAdapter(logger, location, endpoint, altEndpoints, null!);
            }
            else
            {
                _resolvedWellKnown(logger, new Identity(location, category), endpoint, altEndpoints, null!);
            }
        }

        internal static void LogResolving(this ILogger logger, string location, string? category)
        {
            if (category == null)
            {
                _resolvingAdapter(logger, location, null!);
            }
            else
            {
                _resolvingWellKnown(logger, new Identity(location, category), null!);
            }
        }
    }
}
