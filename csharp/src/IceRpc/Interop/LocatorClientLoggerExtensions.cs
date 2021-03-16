// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Interop
{
    /// <summary>This class contains ILogger extensions methods for logging messages in the
    /// "IceRpc.Interop.LocatorClient" category.</summary>
    internal static class LocatorClientLoggerExtensions
    {
        private const int ClearAdapterIdEndpoints = 0;
        private const int ClearWellKnownProxyEndpoints = 1;
        private const int ClearWellKnownProxyWithoutEndpoints = 2;
        private const int CouldNotFindEndpointsForAdapterId = 3;
        private const int CouldNotFindEndpointsForWellKnownProxy = 4;
        private const int FoundEntryForAdapterIdInLocatorCache = 5;
        private const int FoundEntryForWellKnownProxyInLocatorCache = 6;
        private const int InvalidProxyResolvingAdapterId = 7;
        private const int InvalidProxyResolvingProxy = 8;
        private const int ResolveAdapterIdFailure = 9;
        private const int ResolveWellKnownProxyEndpointsFailure = 10;
        private const int ResolvedAdapterId = 11;
        private const int ResolvedWellKnownProxy = 12;
        private const int ResolvingAdapterId = 13;
        private const int ResolvingWellKnownProxy = 14;

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _clearAdapterIdEndpoints =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(ClearAdapterIdEndpoints, nameof(ClearAdapterIdEndpoints)),
                "removed endpoints for location from locator cache adapter ID =  {AdapterId}, endpoints = {Endpoints}");

        private static readonly Action<ILogger, Identity, IReadOnlyList<Endpoint>, Exception> _clearWellKnownProxyEndpoints =
            LoggerMessage.Define<Identity, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(ClearWellKnownProxyEndpoints, nameof(ClearWellKnownProxyEndpoints)),
                "removed well-known proxy with endpoints from locator cache well-known proxy = {identity}, " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, Identity, string, Exception> _clearWellKnownProxyWithoutEndpoints =
            LoggerMessage.Define<Identity, string>(
                LogLevel.Trace,
                new EventId(ClearWellKnownProxyWithoutEndpoints, nameof(ClearWellKnownProxyWithoutEndpoints)),
                "removed well-known proxy without endpoints from locator cache proxy = {identity}, " +
                "adapter ID =  {AdapterId}");

        private static readonly Action<ILogger, string, Exception> _couldNotFindEndpointsForAdapterId =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(CouldNotFindEndpointsForAdapterId, nameof(CouldNotFindEndpointsForAdapterId)),
                "could not find endpoint(s) for adapter ID =  {AdapterId}");

        private static readonly Action<ILogger, Identity, Exception> _couldNotFindEndpointsForWellKnownProxy =
            LoggerMessage.Define<Identity>(
                LogLevel.Debug,
                new EventId(CouldNotFindEndpointsForWellKnownProxy, nameof(CouldNotFindEndpointsForWellKnownProxy)),
                "could not find endpoint(s) for well-known proxy = {Identity}");

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _foundEntryForAdapterIdInLocatorCache =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(FoundEntryForAdapterIdInLocatorCache, nameof(FoundEntryForAdapterIdInLocatorCache)),
                "found entry for location in locator cache"); // TODO

        private static readonly Action<ILogger, Identity, IReadOnlyList<Endpoint>, Exception> _foundEntryForWellKnownProxyInLocatorCache =
            LoggerMessage.Define<Identity, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(FoundEntryForWellKnownProxyInLocatorCache,
                            nameof(FoundEntryForWellKnownProxyInLocatorCache)),
                "found entry for well-known proxy in locator cache well-known proxy = {Identity}, " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, string, ServicePrx, Exception> _invalidProxyResolvingAdapterId =
            LoggerMessage.Define<string, ServicePrx>(
                LogLevel.Debug,
                new EventId(InvalidProxyResolvingAdapterId, nameof(InvalidProxyResolvingAdapterId)),
                "locator returned an invalid proxy when resolving adapter ID =  {AdapterId}, received = {Proxy}");

        private static readonly Action<ILogger, Identity, ServicePrx, Exception> _invalidProxyResolvingProxy =
            LoggerMessage.Define<Identity, ServicePrx>(
                LogLevel.Debug,
                new EventId(InvalidProxyResolvingProxy, nameof(InvalidProxyResolvingProxy)),
                "locator returned an invalid proxy when resolving proxy = {Identity}, received = {Received}");

        private static readonly Action<ILogger, string, Exception> _resolveAdapterIdFailure =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(ResolveAdapterIdFailure, nameof(ResolveAdapterIdFailure)),
                "failure resolving location {AdapterId}");

        private static readonly Action<ILogger, Identity, Exception> _resolveWellKnownProxyEndpointsFailure =
            LoggerMessage.Define<Identity>(
                LogLevel.Debug,
                new EventId(ResolveWellKnownProxyEndpointsFailure, nameof(ResolveWellKnownProxyEndpointsFailure)),
                "failure resolving endpoints for well-known proxy {Identity}");

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _resolvedAdapterId =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                new EventId(ResolvedAdapterId, nameof(ResolvedAdapterId)),
                "resolved location using locator, adding to locator cache adapter ID =  {AdapterId}, " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, Identity, IReadOnlyList<Endpoint>, Exception> _resolvedWellKnownProxy =
            LoggerMessage.Define<Identity, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                new EventId(ResolvedWellKnownProxy, nameof(ResolvedWellKnownProxy)),
                "resolved well-known proxy using locator, adding to locator cache");

        private static readonly Action<ILogger, string, Exception> _resolvingAdapterId = LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(ResolvingAdapterId, nameof(ResolvingAdapterId)),
            "resolving location {AdapterId}");

        private static readonly Action<ILogger, Identity, Exception> _resolvingWellKnownProxy =
            LoggerMessage.Define<Identity>(
                LogLevel.Debug,
                new EventId(ResolvingWellKnownProxy, nameof(ResolvingWellKnownProxy)),
                "resolving well-known object {Identity}");

        internal static void LogClearAdapterIdEndpoints(
            this ILogger logger,
            string location,
            IReadOnlyList<Endpoint> endpoints) =>
            _clearAdapterIdEndpoints(logger, location, endpoints, null!);

        internal static void LogClearWellKnownProxyEndpoints(
            this ILogger logger,
            Identity identity,
            IReadOnlyList<Endpoint> endpoints) =>
            _clearWellKnownProxyEndpoints(logger, identity, endpoints, null!);

        internal static void LogClearWellKnownProxyWithoutEndpoints(
            this ILogger logger,
            Identity identity,
            string location) =>
            _clearWellKnownProxyWithoutEndpoints(logger, identity, location, null!);

        internal static void LogCouldNotFindEndpointsForAdapterId(this ILogger logger, string location) =>
            _couldNotFindEndpointsForAdapterId(logger, location, null!);

        internal static void LogCouldNotFindEndpointsForWellKnownProxy(
            this ILogger logger,
            Identity identity) =>
            _couldNotFindEndpointsForWellKnownProxy(logger, identity, null!);

        internal static void LogFoundEntryForAdapterIdInLocatorCache(
            this ILogger logger,
            string location,
            IReadOnlyList<Endpoint> endpoints) =>
            _foundEntryForAdapterIdInLocatorCache(logger, location, endpoints, null!);

        internal static void LogFoundEntryForWellKnownProxyInLocatorCache(
            this ILogger logger,
            Identity identity,
            IReadOnlyList<Endpoint> endpoints) =>
            _foundEntryForWellKnownProxyInLocatorCache(logger, identity, endpoints, null!);

        internal static void LogInvalidProxyResolvingAdapterId(this ILogger logger, string location, ServicePrx proxy) =>
            _invalidProxyResolvingAdapterId(logger, location, proxy, null!);

        internal static void LogInvalidProxyResolvingProxy(this ILogger logger, Identity identity, ServicePrx received) =>
            _invalidProxyResolvingProxy(logger, identity, received, null!);

        internal static void LogResolveAdapterIdFailure(
            this ILogger logger,
            string location,
            Exception exception) =>
            _resolveAdapterIdFailure(logger, location, exception);

        internal static void LogResolveWellKnownProxyEndpointsFailure(
            this ILogger logger,
            Identity identity,
            Exception exception) =>
            _resolveWellKnownProxyEndpointsFailure(logger, identity, exception);

        internal static void LogResolvedWellKnownProxy(
            this ILogger logger,
            Identity identity,
            IReadOnlyList<Endpoint> endpoints) =>
            _resolvedWellKnownProxy(logger, identity, endpoints, null!);

        internal static void LogResolvedAdapterId(
            this ILogger logger,
            string location,
            IReadOnlyList<Endpoint> endpoints) =>
            _resolvedAdapterId(logger, location, endpoints, null!);

        internal static void LogResolvingAdapterId(this ILogger logger, string location) =>
            _resolvingAdapterId(logger, location, null!);

        internal static void LogResolvingWellKnownProxy(this ILogger logger, Identity identity) =>
            _resolvingWellKnownProxy(logger, identity, null!);
    }
}
