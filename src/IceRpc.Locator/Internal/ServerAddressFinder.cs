// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Locator.Internal;

/// <summary>An server address finder finds the server address(es) of a location. These server address(es) are carried
/// by a dummy service address. When this dummy service address is not null, its ServerAddress property is guaranteed to
/// be not null. Unlike <see cref="ILocationResolver"/>, a server address finder does not provide cache-related
/// parameters and typically does not maintain a cache.</summary>
internal interface IServerAddressFinder
{
    Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancel);
}

/// <summary>The main implementation of IServerAddressFinder. It uses a locator proxy to "find" the server addresses.
/// </summary>
internal class LocatorServerAddressFinder : IServerAddressFinder
{
    private readonly ILocatorProxy _locator;

    public async Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancel)
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

    internal LocatorServerAddressFinder(ILocatorProxy locator) => _locator = locator;
}

/// <summary>A decorator that adds event source logging to a server address finder.</summary>
internal class LogServerAddressFinderDecorator : IServerAddressFinder
{
    private readonly IServerAddressFinder _decoratee;

    public async Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancel)
    {
        ServiceAddress? serviceAddress = null;

        try
        {
            serviceAddress = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);
            return serviceAddress;
        }
        finally
        {
            // We don't log the exception here because we expect another decorator further up in chain to log this
            // exception.
            LocatorEventSource.Log.Find(location, serviceAddress);
        }
    }

    internal LogServerAddressFinderDecorator(IServerAddressFinder decoratee) => _decoratee = decoratee;
}

/// <summary>A decorator that updates its server address cache after a call to its decoratee (e.g. remote locator). It
/// needs to execute downstream from the Coalesce decorator.</summary>
internal class CacheUpdateServerAddressFinderDecorator : IServerAddressFinder
{
    private readonly IServerAddressFinder _decoratee;
    private readonly IServerAddressCache _serverAddressCache;

    public async Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancel)
    {
        ServiceAddress? serviceAddress = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);

        if (serviceAddress is not null)
        {
            _serverAddressCache.Set(location, serviceAddress);
        }
        else
        {
            _serverAddressCache.Remove(location);
        }
        return serviceAddress;
    }

    internal CacheUpdateServerAddressFinderDecorator(
        IServerAddressFinder decoratee,
        IServerAddressCache serverAddressCache)
    {
        _serverAddressCache = serverAddressCache;
        _decoratee = decoratee;
    }
}

/// <summary>A decorator that detects multiple concurrent identical FindAsync and "coalesce" them to avoid overloading
/// the decoratee (e.g. the remote locator).</summary>
internal class CoalesceServerAddressFinderDecorator : IServerAddressFinder
{
    private readonly IServerAddressFinder _decoratee;
    private readonly object _mutex = new();
    private readonly Dictionary<Location, Task<ServiceAddress?>> _requests = new();

    public Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancel)
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

    internal CoalesceServerAddressFinderDecorator(IServerAddressFinder decoratee) =>
        _decoratee = decoratee;
}
