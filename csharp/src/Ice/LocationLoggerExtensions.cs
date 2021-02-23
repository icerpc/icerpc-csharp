// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace ZeroC.Ice
{
    internal static class LocationLoggerExtensions
    {
        private const int ClearLocationEndpoints = 0;
        private const int ClearWellKnownProxyEndpoints = 1;
        private const int ClearWellKnownProxyWithoutEndpoints = 2;
        private const int CouldNotFindEndpointsForLocation = 3;
        private const int CouldNotFindEndpointsForWellKnownProxy = 4;
        private const int FoundEntryForLocationInLocatorCache = 5;
        private const int FoundEntryForWellKnownProxyInLocatorCache = 6;
        private const int InvalidProxyResolvingLocation = 7;
        private const int InvalidProxyResolvingProxy = 8;
        private const int RegisterObjectAdapterEndpointsFailure = 9;
        private const int RegisterObjectAdapterEndpointsSuccess = 10;
        private const int ResolveLocationFailure = 11;
        private const int ResolveWellKnownProxyEndpointsFailure = 12;
        private const int ResolvedLocation = 13;
        private const int ResolvedWellKnownProxy = 14;
        private const int ResolvingLocation = 15;
        private const int ResolvingWellKnownProxy = 16;
        private const int UnregisterObjectAdapterEndpointsFailure = 17;
        private const int UnregisterObjectAdapterEndpointsSuccess = 18;

        private static readonly Action<ILogger, string, string, IReadOnlyList<Endpoint>, Exception> _clearLocationEndpoints =
            LoggerMessage.Define<string, string, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(ClearLocationEndpoints, nameof(ClearLocationEndpoints)),
                "removed endpoints for location from locator cache location = {Location}, protocol = {Protocol}, " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, Identity, string, string, IReadOnlyList<Endpoint>, Exception> _clearWellKnownProxyEndpoints =
            LoggerMessage.Define<Identity, string, string, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(ClearWellKnownProxyEndpoints, nameof(ClearWellKnownProxyEndpoints)),
                "removed well-known proxy with endpoints from locator cache well-known proxy = {identity}, " +
                "facet = {Facet}, protocol = {Protocol}, endpoints = {Endpoints}");

        private static readonly Action<ILogger, Identity, string, string, string, Exception> _clearWellKnownProxyWithoutEndpoints =
            LoggerMessage.Define<Identity, string, string, string>(
                LogLevel.Trace,
                new EventId(ClearWellKnownProxyWithoutEndpoints, nameof(ClearWellKnownProxyWithoutEndpoints)),
                "removed well-known proxy without endpoints from locator cache proxy = {identity}, " +
                "facet = {Facet}, protocol = {Protocol}, location = {Location}");

        private static readonly Action<ILogger, string, Exception> _couldNotFindEndpointsForLocation =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(CouldNotFindEndpointsForLocation, nameof(CouldNotFindEndpointsForLocation)),
                "could not find endpoint(s) for location = {Location}");

        private static readonly Action<ILogger, Identity, string, string, Exception> _couldNotFindEndpointsForWellKnownProxy =
            LoggerMessage.Define<Identity, string, string>(
                LogLevel.Debug,
                new EventId(CouldNotFindEndpointsForWellKnownProxy, nameof(CouldNotFindEndpointsForWellKnownProxy)),
                "could not find endpoint(s) for well-known proxy = {Identity}, facet = {Facet}, protocol = {Protocol})");

        private static readonly Action<ILogger, string, string, IReadOnlyList<Endpoint>, Exception> _foundEntryForLocationInLocatorCache =
            LoggerMessage.Define<string, string, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(FoundEntryForLocationInLocatorCache, nameof(FoundEntryForLocationInLocatorCache)),
                "found entry for location in locator cache");

        private static readonly Action<ILogger, Identity, string, string, IReadOnlyList<Endpoint>, Exception> _foundEntryForWellKnownProxyInLocatorCache =
            LoggerMessage.Define<Identity, string, string, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                new EventId(FoundEntryForWellKnownProxyInLocatorCache,
                            nameof(FoundEntryForWellKnownProxyInLocatorCache)),
                "found entry for well-known proxy in locator cache well-known proxy = {Identity}, " +
                "facet = {Facet}, protocol = {Protocol}, endpoints = {Endpoints}");

        private static readonly Action<ILogger, string, ServicePrx, Exception> _invalidProxyResolvingLocation =
            LoggerMessage.Define<string, ServicePrx>(
                LogLevel.Debug,
                new EventId(InvalidProxyResolvingLocation, nameof(InvalidProxyResolvingLocation)),
                "locator returned an invalid proxy when resolving location = {Location}, received = {Proxy}");

        private static readonly Action<ILogger, Identity, string, ServicePrx, Exception> _invalidProxyResolvingProxy =
            LoggerMessage.Define<Identity, string, ServicePrx>(
                LogLevel.Debug,
                new EventId(InvalidProxyResolvingProxy, nameof(InvalidProxyResolvingProxy)),
                "locator returned an invalid proxy when resolving proxy = {Identity}, facet = {Facet}, " +
                "received = {Received}");

        private static readonly Action<ILogger, string, Exception> _registerObjectAdapterEndpointsFailure =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(RegisterObjectAdapterEndpointsFailure, nameof(RegisterObjectAdapterEndpointsFailure)),
                "failed to register the endpoints of object adapter {ObjectAdapter} with the locator registry");

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _registerObjectAdapterEndpointsSuccess =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                new EventId(RegisterObjectAdapterEndpointsSuccess, nameof(RegisterObjectAdapterEndpointsSuccess)),
                "registered the endpoints of object adapter {ObjectAdapter} with the locator registry " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, string, Exception> _resolveLocationFailure =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(ResolveLocationFailure, nameof(ResolveLocationFailure)),
                "failure resolving location {Location}");

        private static readonly Action<ILogger, Identity, string, string, Exception> _resolveWellKnownProxyEndpointsFailure =
            LoggerMessage.Define<Identity, string, string>(
                LogLevel.Debug,
                new EventId(ResolveWellKnownProxyEndpointsFailure, nameof(ResolveWellKnownProxyEndpointsFailure)),
                "failure resolving endpoints for well-known proxy {Identity}, face = {Facet}, protocol = {Protocol}");

        private static readonly Action<ILogger, string, string, IReadOnlyList<Endpoint>, Exception> _resolvedLocation =
            LoggerMessage.Define<string, string, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                new EventId(ResolvedLocation, nameof(ResolvedLocation)),
                "resolved location using locator, adding to locator cache location = {Location}, " +
                "protocol = {Protocol}, endpoints = {Endpoints}");

        private static readonly Action<ILogger, Identity, string, string, IReadOnlyList<Endpoint>, Exception> _resolvedWellKnownProxy =
            LoggerMessage.Define<Identity, string, string, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                new EventId(ResolvedWellKnownProxy, nameof(ResolvedWellKnownProxy)),
                "resolved well-known proxy using locator, adding to locator cache");

        private static readonly Action<ILogger, string, Exception> _resolvingLocation = LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(ResolvingLocation, nameof(ResolvingLocation)),
            "resolving location {Location}");

        private static readonly Action<ILogger, Identity, string, string, Exception> _resolvingWellKnownProxy =
            LoggerMessage.Define<Identity, string, string>(
                LogLevel.Debug,
                new EventId(ResolvingWellKnownProxy, nameof(ResolvingWellKnownProxy)),
                "resolving well-known object {Identity}, facet = {Facet}");

        private static readonly Action<ILogger, string, Exception> _unregisterObjectAdapterEndpointsFailure =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(UnregisterObjectAdapterEndpointsFailure, nameof(UnregisterObjectAdapterEndpointsFailure)),
                "failed to unregister the endpoints of object adapter {ObjectAdapter} from the locator registry");

        private static readonly Action<ILogger, string, Exception> _unregisterObjectAdapterEndpointsSuccess =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(UnregisterObjectAdapterEndpointsSuccess, nameof(UnregisterObjectAdapterEndpointsSuccess)),
                "unregistered the endpoints of object adapter {ObjectAdapter} from the locator registry");

        internal static void LogClearLocationEndpoints(
            this ILogger logger,
            string location,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _clearLocationEndpoints(logger, location, protocol.GetName(), endpoints, null!);

        internal static void LogClearWellKnownProxyEndpoints(
            this ILogger logger,
            Identity identity,
            string facet,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _clearWellKnownProxyEndpoints(logger, identity, facet, protocol.GetName(), endpoints, null!);

        internal static void LogClearWellKnownProxyWithoutEndpoints(
            this ILogger logger,
            Identity identity,
            string facet,
            Protocol protocol,
            IReadOnlyList<string> location) =>
            _clearWellKnownProxyWithoutEndpoints(logger, identity, facet, protocol.GetName(), location.ToLocationString(), null!);

        internal static void LogCouldNotFindEndpointsForLocation(this ILogger logger, IReadOnlyList<string> location) =>
            _couldNotFindEndpointsForLocation(logger, location.ToLocationString(), null!);

        internal static void LogCouldNotFindEndpointsForWellKnownProxy(
            this ILogger logger,
            Identity identity,
            string facet,
            Protocol protocol) =>
            _couldNotFindEndpointsForWellKnownProxy(logger, identity, facet, protocol.GetName(), null!);

        internal static void LogFoundEntryForLocationInLocatorCache(
            this ILogger logger,
            IReadOnlyList<string> location,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _foundEntryForLocationInLocatorCache(logger, location.ToLocationString(), protocol.GetName(), endpoints, null!);

        internal static void LogFoundEntryForWellKnownProxyInLocatorCache(
            this ILogger logger,
            Identity identity,
            string facet,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _foundEntryForWellKnownProxyInLocatorCache(logger, identity, facet, protocol.GetName(), endpoints, null!);

        internal static void LogInvalidProxyResolvingLocation(this ILogger logger, string location, ServicePrx proxy) =>
            _invalidProxyResolvingLocation(logger, location, proxy, null!);

        internal static void LogInvalidProxyResolvingProxy(this ILogger logger, Identity identity, string facet, ServicePrx received) =>
            _invalidProxyResolvingProxy(logger, identity, facet, received, null!);

        internal static void LogRegisterObjectAdapterEndpointsFailure(
            this ILogger logger,
            ObjectAdapter adapter,
            Exception ex) =>
            _registerObjectAdapterEndpointsFailure(logger, adapter.Name, ex);

        internal static void LogRegisterObjectAdapterEndpointsSuccess(
            this ILogger logger,
            ObjectAdapter adapter,
            IReadOnlyList<Endpoint> endpoints) =>
            _registerObjectAdapterEndpointsSuccess(logger, adapter.Name, endpoints, null!);

        internal static void LogResolveLocationFailure(
            this ILogger logger,
            IReadOnlyList<string> location,
            Exception exception) =>
            _resolveLocationFailure(logger, location.ToLocationString(), exception);

        internal static void LogResolveWellKnownProxyEndpointsFailure(
            this ILogger logger,
            Identity identity,
            string facet,
            Protocol protocol,
            Exception exception) =>
            _resolveWellKnownProxyEndpointsFailure(logger, identity, facet, protocol.GetName(), exception);

        internal static void LogResolvedWellKnownProxy(
            this ILogger logger,
            Identity identity,
            string facet,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _resolvedWellKnownProxy(logger, identity, facet, protocol.GetName(), endpoints, null!);

        internal static void LogResolvedLocation(
            this ILogger logger,
            IReadOnlyList<string> location,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _resolvedLocation(logger, location.ToLocationString(), protocol.GetName(), endpoints, null!);

        internal static void LogResolvingLocation(this ILogger logger, IReadOnlyList<string> location) =>
            _resolvingLocation(logger, location.ToLocationString(), null!);

        internal static void LogResolvingWellKnownProxy(this ILogger logger, Identity identity, string facet, Protocol protocol) =>
            _resolvingWellKnownProxy(logger, identity, facet, protocol.GetName(), null!);

        internal static void LogUnregisterObjectAdapterEndpointsFailure(
            this ILogger logger,
            ObjectAdapter adapter,
            Exception ex) =>
            _unregisterObjectAdapterEndpointsFailure(logger, adapter.Name, ex);

        internal static void LogUnregisterObjectAdapterEndpointsSuccess(this ILogger logger, ObjectAdapter adapter) =>
            _unregisterObjectAdapterEndpointsSuccess(logger, adapter.Name, null!);
    }
}
