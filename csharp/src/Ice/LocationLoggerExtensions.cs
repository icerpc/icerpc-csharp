// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace ZeroC.Ice
{
    internal static partial class LocationLoggerExtensions
    {
        private static readonly Action<ILogger, string, Protocol, IReadOnlyList<Endpoint>, Exception> _clearLocationEndpoints =
            LoggerMessage.Define<string, Protocol, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                GetEventId(LocationEvent.ClearLocationEndpoints),
                "removed endpoints for location from locator cache location = {Location}, protocol = {Protocol}, " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, ObjectPrx, IReadOnlyList<Endpoint>, Exception> _clearWellKnownProxyEndpoints =
            LoggerMessage.Define<ObjectPrx, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                GetEventId(LocationEvent.ClearWellKnownProxyEndpoints),
                "removed well-known proxy with endpoints from locator cache well-known proxy = {Proxy}, " +
                "endpoints {Endpoints}");

        private static readonly Action<ILogger, ObjectPrx, string, Exception> _clearWellKnownProxyWithoutEndpoints =
            LoggerMessage.Define<ObjectPrx, string>(
                LogLevel.Trace,
                GetEventId(LocationEvent.ClearWellKnownProxyWithoutEndpoints),
                "removed well-known proxy without endpoints from locator cache proxy = {Proxy}, location = {Location}");

        private static readonly Action<ILogger, string, Exception> _couldNotFindEndpointsForLocation =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                GetEventId(LocationEvent.CouldNotFindEndpointsForLocation),
                "could not find endpoint(s) for location = {Location}");

        private static readonly Action<ILogger, ObjectPrx, Exception> _couldNotFindEndpointsForWellKnownProxy =
            LoggerMessage.Define<ObjectPrx>(
                LogLevel.Debug,
                GetEventId(LocationEvent.CouldNotFindEndpointsForWellKnownProxy),
                "could not find endpoint(s) for well-known proxy = {Proxy}");

        private static readonly Action<ILogger, string, Protocol, IReadOnlyList<Endpoint>, Exception> _foundEntryForLocationInLocatorCache =
            LoggerMessage.Define<string, Protocol, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                GetEventId(LocationEvent.FoundEntryForLocationInLocatorCache),
                "found entry for location in locator cache");

        private static readonly Action<ILogger, ObjectPrx, IReadOnlyList<Endpoint>, Exception> _foundEntryForWellKnownProxyInLocatorCache =
            LoggerMessage.Define<ObjectPrx, IReadOnlyList<Endpoint>>(
                LogLevel.Trace,
                GetEventId(LocationEvent.FoundEntryForWellKnownProxyInLocatorCache),
                "found entry for well-known proxy in locator cache well-known proxy = {Proxy}, " +
                "endpoints = {Endpoints}");

        private static readonly Action<ILogger, Exception> _getLocatorRegistryFailure = LoggerMessage.Define(
            LogLevel.Error,
            GetEventId(LocationEvent.GetLocatorRegistryFailure),
            "failed to retrieve locator registry");

        private static readonly Action<ILogger, string, ObjectPrx, Exception> _invalidProxyResolvingLocation =
            LoggerMessage.Define<string, ObjectPrx>(
                LogLevel.Debug,
                GetEventId(LocationEvent.InvalidProxyResolvingLocation),
                "locator returned an invalid proxy when resolving location = {Location}, received = {Proxy}");

        private static readonly Action<ILogger, ObjectPrx, ObjectPrx, Exception> _invalidProxyResolvingProxy =
            LoggerMessage.Define<ObjectPrx, ObjectPrx>(
                LogLevel.Debug,
                GetEventId(LocationEvent.InvalidProxyResolvingProxy),
                "locator returned an invalid proxy when resolving proxy = {Proxy}, received = {Received}");

        private static readonly Action<ILogger, string, Exception> _registerObjectAdapterEndpointsFailure =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                GetEventId(LocationEvent.RegisterObjectAdapterEndpointsFailure),
                "failed to register the endpoints of object adapter {ObjectAdapter} with the locator registry");

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _registerObjectAdapterEndpointsSuccess =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                GetEventId(LocationEvent.RegisterObjectAdapterEndpointsSuccess),
                "registered the endpoints of object adapter {ObjectAdapter} with the locator registry endpoints = {Endpoints}");

        private static readonly Action<ILogger, string, Exception> _resolveLocationFailure = LoggerMessage.Define<string>(
            LogLevel.Debug,
            GetEventId(LocationEvent.ResolveLocationFailure),
            "failure resolving location {Location}");

        private static readonly Action<ILogger, ObjectPrx, Exception> _resolveWellKnownProxyEndpointsFailure =
            LoggerMessage.Define<ObjectPrx>(
                LogLevel.Debug,
                GetEventId(LocationEvent.ResolveWellKnownProxyEndpointsFailure),
                "failure resolving endpoints for well-known proxy {Proxy}");

        private static readonly Action<ILogger, string, Protocol, IReadOnlyList<Endpoint>, Exception> _resolvedLocation =
            LoggerMessage.Define<string, Protocol, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                GetEventId(LocationEvent.ResolvedLocation),
                "resolved location using locator, adding to locator cache location = {Location}, " +
                "protocol = {Protocol}, endpoints = {Endpoints}");

        private static readonly Action<ILogger, ObjectPrx, IReadOnlyList<Endpoint>, Exception> _resolvedWellKnownProxy =
            LoggerMessage.Define<ObjectPrx, IReadOnlyList<Endpoint>>(
                LogLevel.Debug,
                GetEventId(LocationEvent.ResolvedWellKnownProxy),
                "resolved well-known proxy using locator, adding to locator cache");

        private static readonly Action<ILogger, string, Exception> _resolvingLocation = LoggerMessage.Define<string>(
            LogLevel.Debug,
            GetEventId(LocationEvent.ResolvingLocation),
            "resolving location {Location}");

        private static readonly Action<ILogger, ObjectPrx, Exception> _resolvingWellKnownProxy = LoggerMessage.Define<ObjectPrx>(
            LogLevel.Debug,
            GetEventId(LocationEvent.ResolvingWellKnownProxy),
            "resolving well-known object {Proxy}");

        private static readonly Action<ILogger, string, Exception> _unregisterObjectAdapterEndpointsFailure =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                GetEventId(LocationEvent.UnregisterObjectAdapterEndpointsFailure),
                "failed to unregister the endpoints of object adapter {ObjectAdapter} from the locator registry");

        private static readonly Action<ILogger, string, Exception> _unregisterObjectAdapterEndpointsSuccess =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                GetEventId(LocationEvent.UnregisterObjectAdapterEndpointsSuccess),
                "unregistered the endpoints of object adapter {ObjectAdapter} from the locator registry");

        private static EventId GetEventId(LocationEvent e) => new EventId((int)e, e.ToString());

        internal static void LogClearLocationEndpoints(
            this ILogger logger,
            string location,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _clearLocationEndpoints(logger, location, protocol, endpoints, null!);

        internal static void LogClearWellKnownProxyEndpoints(
            this ILogger logger,
            ObjectPrx proxy,
            IReadOnlyList<Endpoint> endpoints) =>
            _clearWellKnownProxyEndpoints(logger, proxy, endpoints, null!);

        internal static void LogClearWellKnownProxyWithoutEndpoints(
            this ILogger logger,
            ObjectPrx proxy,
            IReadOnlyList<string> location) =>
            _clearWellKnownProxyWithoutEndpoints(logger, proxy, location.ToLocationString(), null!);

        internal static void LogCouldNotFindEndpointsForLocation(
            this ILogger logger,
            IReadOnlyList<string> location) =>
            _couldNotFindEndpointsForLocation(logger, location.ToLocationString(), null!);

        internal static void LogCouldNotFindEndpointsForWellKnownProxy(this ILogger logger, ObjectPrx proxy) =>
            _couldNotFindEndpointsForWellKnownProxy(logger, proxy, null!);

        internal static void LogFoundEntryForLocationInLocatorCache(
            this ILogger logger,
            IReadOnlyList<string> location,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _foundEntryForLocationInLocatorCache(logger, location.ToLocationString(), protocol, endpoints, null!);

        internal static void LogFoundEntryForWellKnownProxyInLocatorCache(
            this ILogger logger,
            ObjectPrx proxy,
            IReadOnlyList<Endpoint> endpoints) =>
            _foundEntryForWellKnownProxyInLocatorCache(logger, proxy, endpoints, null!);

        internal static void LogGetLocatorRegistryFailure(this ILogger logger, Exception ex) =>
            _getLocatorRegistryFailure(logger, ex);

        internal static void LogInvalidProxyResolvingLocation(
            this ILogger logger,
            string location,
            ObjectPrx proxy) =>
            _invalidProxyResolvingLocation(logger, location, proxy, null!);

        internal static void LogInvalidProxyResolvingProxy(
            this ILogger logger,
            ObjectPrx proxy,
            ObjectPrx received) =>
            _invalidProxyResolvingProxy(logger, proxy, received, null!);

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
            ObjectPrx proxy,
            Exception exception) =>
            _resolveWellKnownProxyEndpointsFailure(logger, proxy, exception);

        internal static void LogResolvedWellKnownProxy(
            this ILogger logger,
            ObjectPrx proxy,
            IReadOnlyList<Endpoint> endpoints) =>
            _resolvedWellKnownProxy(logger, proxy, endpoints, null!);

        internal static void LogResolvedLocation(
            this ILogger logger,
            IReadOnlyList<string> location,
            Protocol protocol,
            IReadOnlyList<Endpoint> endpoints) =>
            _resolvedLocation(logger, location.ToLocationString(), protocol, endpoints, null!);

        internal static void LogResolvingLocation(this ILogger logger, IReadOnlyList<string> location) =>
            _resolvingLocation(logger, location.ToLocationString(), null!);

        internal static void LogResolvingWellKnownProxy(
            this ILogger logger,
            ObjectPrx proxy) =>
            _resolvingWellKnownProxy(logger, proxy, null!);

        internal static void LogUnregisterObjectAdapterEndpointsFailure(
            this ILogger logger,
            ObjectAdapter adapter,
            Exception ex) =>
            _unregisterObjectAdapterEndpointsFailure(logger, adapter.Name, ex);

        internal static void LogUnregisterObjectAdapterEndpointsSuccess(this ILogger logger, ObjectAdapter adapter) =>
            _unregisterObjectAdapterEndpointsSuccess(logger, adapter.Name, null!);

        private enum LocationEvent
        {
            ClearLocationEndpoints = 10001,
            ClearWellKnownProxyEndpoints,
            ClearWellKnownProxyWithoutEndpoints,
            CouldNotFindEndpointsForLocation,
            CouldNotFindEndpointsForWellKnownProxy,
            FoundEntryForLocationInLocatorCache,
            FoundEntryForWellKnownProxyInLocatorCache,
            GetLocatorRegistryFailure,
            InvalidProxyResolvingLocation,
            InvalidProxyResolvingProxy,
            RegisterObjectAdapterEndpointsFailure,
            RegisterObjectAdapterEndpointsSuccess,
            ResolveLocationFailure,
            ResolveWellKnownProxyEndpointsFailure,
            ResolvedLocation,
            ResolvedWellKnownProxy,
            ResolvingLocation,
            ResolvingWellKnownProxy,
            UnregisterObjectAdapterEndpointsFailure,
            UnregisterObjectAdapterEndpointsSuccess,
        }
    }
}
