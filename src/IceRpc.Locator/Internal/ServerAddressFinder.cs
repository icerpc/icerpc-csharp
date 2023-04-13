// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by <see cref="LogServerAddressFinderDecorator"/>.
/// </summary>
internal static partial class ServerAddressFinderLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventId.FindFailed,
        EventName = nameof(LocationEventId.FindFailed),
        Level = LogLevel.Trace,
        Message = "Failed to find {LocationKind} '{Location}'")]
    internal static partial void LogFindFailed(
        this ILogger logger,
        string locationKind,
        Location location);

    [LoggerMessage(
        EventId = (int)LocationEventId.Found,
        EventName = nameof(LocationEventId.Found),
        Level = LogLevel.Trace,
        Message = "Found {LocationKind} '{Location}' = '{ServiceAddress}'")]
    internal static partial void LogFound(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);
}

/// <summary>A server address finder finds the server address(es) of a location. These server address(es) are carried by
/// a dummy service address. When this dummy service address is not null, its ServerAddress property is guaranteed to be
/// not <see langword="null" />. Unlike <see cref="ILocationResolver" />, a server address finder does not provide
/// cache-related parameters and typically does not maintain a cache.</summary>
internal interface IServerAddressFinder
{
    Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancellationToken);
}

/// <summary>The main implementation of IServerAddressFinder. It uses an <see cref="ILocator"/> to "find" the server
/// addresses.</summary>
internal class LocatorServerAddressFinder : IServerAddressFinder
{
    private readonly ILocator _locator;

    public async Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancellationToken)
    {
        if (location.IsAdapterId)
        {
            try
            {
                ServiceAddress? serviceAddress = await _locator.FindAdapterByIdAsync(
                    location.Value,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (serviceAddress is not null)
                {
                    return serviceAddress.Protocol == Protocol.Ice && serviceAddress.ServerAddress is not null ?
                        serviceAddress :
                        throw new InvalidDataException(
                            $"The locator returned invalid service address '{serviceAddress}' when looking up an adapter by ID.");
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
                ServiceAddress? serviceAddress = await _locator.FindObjectByIdAsync(
                    location.Value,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (serviceAddress is not null)
                {
                    // findObjectById can return an indirect service address with an adapter ID
                    return serviceAddress.Protocol == Protocol.Ice &&
                        (serviceAddress.ServerAddress is not null || serviceAddress.Params.ContainsKey("adapter-id")) ?
                            serviceAddress :
                            throw new InvalidDataException(
                                $"The locator returned invalid service address '{serviceAddress}' when looking up an object by ID.");
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

    internal LocatorServerAddressFinder(ILocator locator) => _locator = locator;
}

/// <summary>A decorator that adds logging to a server address finder.</summary>
internal class LogServerAddressFinderDecorator : IServerAddressFinder
{
    private readonly IServerAddressFinder _decoratee;
    private readonly ILogger _logger;

    public async Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancellationToken)
    {
        // We don't log any exceptions here because we expect another decorator further up in chain to log these
        // exceptions.
        ServiceAddress? serviceAddress = await _decoratee.FindAsync(location, cancellationToken).ConfigureAwait(false);
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

    internal LogServerAddressFinderDecorator(IServerAddressFinder decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}

/// <summary>A decorator that updates its server address cache after a call to its decoratee (e.g. remote locator). It
/// needs to execute downstream from the Coalesce decorator.</summary>
internal class CacheUpdateServerAddressFinderDecorator : IServerAddressFinder
{
    private readonly IServerAddressFinder _decoratee;
    private readonly IServerAddressCache _serverAddressCache;

    public async Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancellationToken)
    {
        ServiceAddress? serviceAddress = await _decoratee.FindAsync(location, cancellationToken).ConfigureAwait(false);

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

    public Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancellationToken)
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

        return task.WaitAsync(cancellationToken);

        async Task<ServiceAddress?> PerformFindAsync()
        {
            try
            {
                return await _decoratee.FindAsync(location, cancellationToken).ConfigureAwait(false);
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
