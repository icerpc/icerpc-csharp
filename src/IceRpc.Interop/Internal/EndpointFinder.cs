// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>An endpoint finder finds the endpoint(s) of a location. These endpoint(s) are carried by a dummy proxy.
    /// When this dummy proxy is not null, its Endpoint property is guaranteed to be not null. Unlike
    /// <see cref="ILocationResolver"/>, an endpoint finder does not provide cache-related parameters and typically
    /// does not maintain a cache.</summary>
    internal interface IEndpointFinder
    {
        Task<Proxy?> FindAsync(Location location, CancellationToken cancel);
    }

    /// <summary>The main implementation of IEndpointFinder. It uses a locator proxy to "find" the endpoints.</summary>
    internal class LocatorEndpointFinder : IEndpointFinder
    {
        private readonly ILocatorPrx _locator;

        internal LocatorEndpointFinder(ILocatorPrx locator) => _locator = locator;

        async Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
        {
            if (location.Category == null)
            {
                try
                {
                    ServicePrx? prx =
                        await _locator.FindAdapterByIdAsync(location.AdapterId, cancel: cancel).ConfigureAwait(false);

                    if (prx?.Proxy is Proxy proxy)
                    {
                        if (proxy.Endpoint == null || proxy.Endpoint.Transport == TransportNames.Loc)
                        {
                            throw new InvalidDataException($"findAdapterById returned invalid proxy '{proxy}'");
                        }
                        return proxy;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (AdapterNotFoundException)
                {
                    // We treat AdapterNotFoundException just like a null return value.
                    return null;
                }
            }
            else
            {
                try
                {
                    ServicePrx? prx =
                        await _locator.FindObjectByIdAsync(location.ToIdentity(), cancel: cancel).ConfigureAwait(false);

                    if (prx?.Proxy is Proxy proxy)
                    {
                        if (proxy.Endpoint == null || proxy.Protocol != Protocol.Ice1)
                        {
                            throw new InvalidDataException($"findObjectById returned invalid proxy '{proxy}'");
                        }
                        return proxy;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (ObjectNotFoundException)
                {
                    // We treat ObjectNotFoundException just like a null return value.
                    return null;
                }
            }
        }
    }

    /// <summary>A decorator that adds logging to an endpoint finder.</summary>
    internal class LogEndpointFinderDecorator : IEndpointFinder
    {
        private readonly IEndpointFinder _decoratee;
        private readonly ILogger _logger;

        internal LogEndpointFinderDecorator(IEndpointFinder decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        async Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
        {
            try
            {
                Proxy? proxy = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);

                if (proxy != null)
                {
                    Debug.Assert(proxy.Endpoint != null);
                    _logger.LogFound(location.Kind, location, proxy);
                }
                else
                {
                    _logger.LogFindFailed(location.Kind, location);
                }
                return proxy;
            }
            catch
            {
                // We don't log the exception here because we expect another logger further up in chain to log this
                // exception.
                _logger.LogFindFailed(location.Kind, location);
                throw;
            }
        }
    }

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
            Message = "found {LocationKind} '{Location}' = '{Proxy}'")]
        internal static partial void LogFound(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);
    }

    /// <summary>A decorator that updates its endpoint cache after a call to its decoratee (e.g. remote locator). It
    /// needs to execute downstream from the Coalesce decorator.</summary>
    internal class CacheUpdateEndpointFinderDecorator : IEndpointFinder
    {
        private readonly IEndpointFinder _decoratee;
        private readonly IEndpointCache _endpointCache;

        internal CacheUpdateEndpointFinderDecorator(IEndpointFinder decoratee, IEndpointCache endpointCache)
        {
            _endpointCache = endpointCache;
            _decoratee = decoratee;
        }

        async Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
        {
            Proxy? proxy = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);

            if (proxy != null)
            {
                _endpointCache.Set(location, proxy);
            }
            else
            {
                _endpointCache.Remove(location);
            }
            return proxy;
        }
    }

    /// <summary>A decorator that detects multiple concurrent identical FindAsync and "coalesce" them to avoid
    /// overloading the decoratee (e.g. the remote locator).</summary>
    internal class CoalesceEndpointFinderDecorator : IEndpointFinder
    {
        private readonly IEndpointFinder _decoratee;
        private readonly object _mutex = new();
        private readonly Dictionary<Location, Task<Proxy?>> _requests = new();

        internal CoalesceEndpointFinderDecorator(IEndpointFinder decoratee) =>
            _decoratee = decoratee;

        Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
        {
            Task<Proxy?>? task;

            lock (_mutex)
            {
                if (!_requests.TryGetValue(location, out task))
                {
                    // If there is no request in progress, we invoke one and cache the request to prevent concurrent
                    // identical requests. It's removed once the response is received.
                    task = PerformFindAsync();

                    if (!task.IsCompleted)
                    {
                        // If PerformFindAsync completed, don't add the task (it would leak since PerformFindAsync
                        // is responsible for removing it).
                        // Since PerformFindAsync locks _mutex in its finally block, the only way it can
                        // be completed now is if completed synchronously.
                        _requests.Add(location, task);
                    }
                }
            }

            return task.WaitAsync(cancel);

            async Task<Proxy?> PerformFindAsync()
            {
                try
                {
                    return await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);
                }
                finally
                {
                    lock (_mutex)
                    {
                        _requests.Remove(location);
                    }
                }
            }
        }
    }
}
