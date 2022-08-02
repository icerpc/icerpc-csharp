// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>An server address finder finds the server address(es) of a location. These server address(es) are carried by a dummy service
/// address. When this dummy service address is not null, its ServerAddress property is guaranteed to be not null.
/// Unlike <see cref="ILocationResolver"/>, a server address finder does not provide cache-related parameters and typically
/// does not maintain a cache.</summary>
internal interface IEndpointFinder
{
    Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancel);
}

/// <summary>The main implementation of IEndpointFinder. It uses a locator proxy to "find" the server addresses.</summary>
internal class LocatorEndpointFinder : IEndpointFinder
{
    private readonly ILocatorProxy _locator;

    internal LocatorEndpointFinder(ILocatorProxy locator) => _locator = locator;

    async Task<ServiceAddress?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
    {
        if (location.IsAdapterId)
        {
            try
            {
                ServiceProxy? proxy =
                    await _locator.FindAdapterByIdAsync(location.Value, cancel: cancel).ConfigureAwait(false);

                if (proxy?.ServiceAddress is ServiceAddress serviceAddress)
                {
                    return serviceAddress.Protocol == Protocol.Ice && serviceAddress.ServerAddress is not null ?
                        serviceAddress :
                        throw new InvalidDataException(
                            $"findAdapterById returned invalid service address '{serviceAddress}'");
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
                ServiceProxy? proxy =
                    await _locator.FindObjectByIdAsync(location.Value, cancel: cancel).ConfigureAwait(false);

                if (proxy?.ServiceAddress is ServiceAddress serviceAddress)
                {
                    // findObjectById can return an indirect service address with an adapter ID
                    return serviceAddress.Protocol == Protocol.Ice &&
                        (serviceAddress.ServerAddress is not null || serviceAddress.Params.ContainsKey("adapter-id")) ?
                            serviceAddress :
                            throw new InvalidDataException(
                                $"findObjectById returned invalid service address '{serviceAddress}'");
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

/// <summary>A decorator that adds logging to a server address finder.</summary>
internal class LogEndpointFinderDecorator : IEndpointFinder
{
    private readonly IEndpointFinder _decoratee;
    private readonly ILogger _logger;

    internal LogEndpointFinderDecorator(IEndpointFinder decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }

    async Task<ServiceAddress?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
    {
        try
        {
            ServiceAddress? serviceAddress = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);

            if (serviceAddress is not null)
            {
                _logger.LogFound(location.Kind, location, serviceAddress);
            }
            else
            {
                _logger.LogFindFailed(location.Kind, location);
            }
            return serviceAddress;
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

/// <summary>A decorator that updates its server address cache after a call to its decoratee (e.g. remote locator). It
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

    async Task<ServiceAddress?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
    {
        ServiceAddress? serviceAddress = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);

        if (serviceAddress is not null)
        {
            _endpointCache.Set(location, serviceAddress);
        }
        else
        {
            _endpointCache.Remove(location);
        }
        return serviceAddress;
    }
}

/// <summary>A decorator that detects multiple concurrent identical FindAsync and "coalesce" them to avoid
/// overloading the decoratee (e.g. the remote locator).</summary>
internal class CoalesceEndpointFinderDecorator : IEndpointFinder
{
    private readonly IEndpointFinder _decoratee;
    private readonly object _mutex = new();
    private readonly Dictionary<Location, Task<ServiceAddress?>> _requests = new();

    internal CoalesceEndpointFinderDecorator(IEndpointFinder decoratee) =>
        _decoratee = decoratee;

    Task<ServiceAddress?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
    {
        Task<ServiceAddress?>? task;

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

        async Task<ServiceAddress?> PerformFindAsync()
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
