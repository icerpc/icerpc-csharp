// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A connection pool is an invoker that routes outgoing requests to connections it manages. This routing is
/// based on the <see cref="IEndpointFeature"/> and the endpoints of the service address carried by each outgoing
/// request.</summary>
public sealed class ConnectionPool : IInvoker, IAsyncDisposable
{
    // Connected connections that can be returned immediately.
    private readonly Dictionary<Endpoint, IProtocolConnection> _activeConnections = new(EndpointComparer.ParameterLess);

    private bool _isDisposed;
    private bool _isReadOnly;

    private readonly ILoggerFactory? _loggerFactory;
    private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;

    private readonly object _mutex = new();

    private readonly ConnectionPoolOptions _options;

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<Endpoint, IProtocolConnection> _pendingConnections = new(EndpointComparer.ParameterLess);

    // Formerly pending or active connections that are closed but not shutdown yet.
    private readonly HashSet<IProtocolConnection> _shutdownPendingConnections = new();

    private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;

    /// <summary>Constructs a connection pool.</summary>
    /// <param name="options">The connection pool options.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
    /// </param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol
    /// connections.</param>
    /// <param name="simpleClientTransport">The simple transport used to create ice protocol connections.</param>
    public ConnectionPool(
        ConnectionPoolOptions options,
        ILoggerFactory? loggerFactory = null,
        IClientTransport<IMultiplexedNetworkConnection>? multiplexedClientTransport = null,
        IClientTransport<ISimpleNetworkConnection>? simpleClientTransport = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _multiplexedClientTransport = multiplexedClientTransport ?? ClientConnection.DefaultMultiplexedClientTransport;
        _simpleClientTransport = simpleClientTransport ?? ClientConnection.DefaultSimpleClientTransport;
    }

    /// <summary>Constructs a connection pool.</summary>
    /// <param name="clientConnectionOptions">The client connection options for connections created by this pool.
    /// </param>
    public ConnectionPool(ClientConnectionOptions clientConnectionOptions)
        : this(new ConnectionPoolOptions { ClientConnectionOptions = clientConnectionOptions })
    {
    }

    /// <summary>Releases all resources allocated by this connection pool.</summary>
    /// <returns>A value task that completes when all connections managed by this pool are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _isDisposed = true;
            _isReadOnly = true;
        }

        // Dispose all connections managed by this pool.
        IEnumerable<IProtocolConnection> allConnections =
            _pendingConnections.Values.Concat(_activeConnections.Values).Concat(_shutdownPendingConnections);

        await Task.WhenAll(allConnections.Select(connection => connection.DisposeAsync().AsTask()))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        CheckIfDisposed();

        if (request.Features.Get<IEndpointFeature>() is not IEndpointFeature endpointFeature)
        {
            endpointFeature = new EndpointFeature(request.ServiceAddress);
            request.Features = request.Features.With(endpointFeature);
        }

        if (endpointFeature.Endpoint is null)
        {
            throw new NoEndpointException(request.ServiceAddress);
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            IProtocolConnection connection = await GetConnectionAsync(endpointFeature, cancel).ConfigureAwait(false);
            return await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Gracefully shuts down all connections managed by this pool. This method can be called multiple times.
    /// </summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancel = default)
    {
        CheckIfDisposed();

        lock (_mutex)
        {
            _isReadOnly = true;
        }

        // Shut down all connections managed by this pool.
        IEnumerable<IProtocolConnection> allConnections =
            _pendingConnections.Values.Concat(_activeConnections.Values).Concat(_shutdownPendingConnections);

        return Task.WhenAll(
            allConnections.Select(connection => connection.ShutdownAsync("connection pool shutdown", cancel)));
    }

    /// <summary>Checks with the protocol-dependent transport if this endpoint has valid parameters. We call this method
    /// when it appears we can reuse an active or pending connection based on a parameterless endpoint match.</summary>
    /// <param name="endpoint">The endpoint to check.</param>
    private void CheckEndpoint(Endpoint endpoint)
    {
        bool isValid = endpoint.Protocol == Protocol.Ice ?
            _simpleClientTransport.CheckParams(endpoint) :
            _multiplexedClientTransport.CheckParams(endpoint);

        if (!isValid)
        {
            throw new FormatException(
                $"cannot establish a client connection to endpoint '{endpoint}': one or more parameters are invalid");
        }
    }

    private void CheckIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionPool));
        }
    }

    /// <summary>Creates a connection and attempts to connect this connection unless there is an active or pending
    /// connection for the desired endpoint.</summary>
    /// <param name="endpoint">The endpoint of the server.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A connected connection.</returns>
    private async ValueTask<IProtocolConnection> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
    {
        IProtocolConnection? connection = null;
        bool created = false;

        lock (_mutex)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("pool shutting down");
            }

            if (_activeConnections.TryGetValue(endpoint, out connection))
            {
                CheckEndpoint(endpoint);
                return connection;
            }
            else if (_pendingConnections.TryGetValue(endpoint, out connection))
            {
                CheckEndpoint(endpoint);
                // and call ConnectAsync on this connection after the if block.
            }
            else
            {
                connection = ProtocolConnection.CreateClientConnection(
                    _options.ClientConnectionOptions with { Endpoint = endpoint },
                    _loggerFactory,
                    _multiplexedClientTransport,
                    _simpleClientTransport);

                created = true;
                _pendingConnections.Add(endpoint, connection);
            }
        }

        if (created)
        {
            try
            {
                // TODO: add cancellation token to cancel when ConnectionPool is shut down / disposed.
                await connection.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch
            {
                bool scheduleRemoveFromClosed = false;

                lock (_mutex)
                {
                    // the _pendingConnections collection is read-only after shutdown
                    if (!_isReadOnly)
                    {
                        // "move" from pending to shutdown pending
                        bool removed = _pendingConnections.Remove(endpoint);
                        Debug.Assert(removed);
                        _ = _shutdownPendingConnections.Add(connection);
                        scheduleRemoveFromClosed = true;
                    }
                }
                if (scheduleRemoveFromClosed)
                {
                    _ = RemoveFromClosedAsync(connection, graceful: false);
                }

                throw;
            }

            bool scheduleRemoveFromActive = false;

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // "move" from pending to active
                    bool removed = _pendingConnections.Remove(endpoint);
                    Debug.Assert(removed);
                    _activeConnections.Add(endpoint, connection);
                    scheduleRemoveFromActive = true;
                }
                // this new connection is being shut down already
            }

            if (scheduleRemoveFromActive)
            {
                // Schedule removal after addition. We do this outside the mutex lock otherwise RemoveFromActive could
                // call await ShutdownAsync or DisposeAsync on the connection within this lock.
                connection.OnAbort(exception => RemoveFromActive(endpoint, graceful: false));
                connection.OnShutdown(message => RemoveFromActive(endpoint, graceful: true));
            }
        }
        else
        {
            await connection.ConnectAsync(cancel).ConfigureAwait(false);
        }

        return connection;

        void RemoveFromActive(Endpoint endpoint, bool graceful)
        {
            Debug.Assert(connection is not null);

            bool scheduleRemoveFromClosed = false;

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // "move" from active to shutdown pending
                    bool removed = _activeConnections.Remove(endpoint);
                    Debug.Assert(removed);
                    _ = _shutdownPendingConnections.Add(connection);
                    scheduleRemoveFromClosed = true;
                }
            }

            if (scheduleRemoveFromClosed)
            {
                _ = RemoveFromClosedAsync(connection, graceful);
            }
        }

        // Remove connection from _shutdownPendingConnections once the dispose is complete
        async Task RemoveFromClosedAsync(IProtocolConnection protocolConnection, bool graceful)
        {
            if (graceful)
            {
                // wait for current shutdown to complete
                try
                {
                    await protocolConnection.ShutdownAsync("", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            await protocolConnection.DisposeAsync().ConfigureAwait(false);

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    _ = _shutdownPendingConnections.Remove(protocolConnection);
                }
            }
        }
    }

    /// <summary>Returns a protocol connection to one of the specified endpoints.</summary>
    /// <param name="endpointFeature">The endpoint feature.</param>
    /// <param name="cancel">The cancellation token.</param>
    private ValueTask<IProtocolConnection> GetConnectionAsync(
        IEndpointFeature endpointFeature,
        CancellationToken cancel)
    {
        IProtocolConnection? connection = null;
        Endpoint mainEndpoint = endpointFeature.Endpoint!.Value;

        if (_options.PreferExistingConnection)
        {
            lock (_mutex)
            {
                connection = GetActiveConnection(mainEndpoint);
                if (connection is null)
                {
                    for (int i = 0; i < endpointFeature.AltEndpoints.Count; ++i)
                    {
                        Endpoint altEndpoint = endpointFeature.AltEndpoints[i];
                        connection = GetActiveConnection(altEndpoint);
                        if (connection is not null)
                        {
                            // This altEndpoint becomes the main endpoint, and the existing main endpoint becomes
                            // the first alt endpoint.
                            endpointFeature.AltEndpoints = endpointFeature.AltEndpoints
                                .RemoveAt(i)
                                .Insert(0, mainEndpoint);
                            endpointFeature.Endpoint = altEndpoint;

                            break; // foreach
                        }
                    }
                }
            }
            if (connection is not null)
            {
                return new(connection);
            }
        }

        return GetOrCreateAsync();

        IProtocolConnection? GetActiveConnection(Endpoint endpoint)
        {
            if (_activeConnections.TryGetValue(endpoint, out IProtocolConnection? connection))
            {
                CheckEndpoint(endpoint);
                return connection;
            }
            else
            {
                return null;
            }
        }

        // Retrieve a pending connection and wait for its ConnectAsync to complete successfully, or create and connect
        // a brand new connection.
        async ValueTask<IProtocolConnection> GetOrCreateAsync()
        {
            try
            {
                return await ConnectAsync(mainEndpoint, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                List<Exception>? exceptionList = null;

                for (int i = 0; i < endpointFeature.AltEndpoints.Count; ++i)
                {
                    Endpoint altEndpoint = endpointFeature.AltEndpoints[i];
                    try
                    {
                        IProtocolConnection connection = await ConnectAsync(altEndpoint, cancel)
                            .ConfigureAwait(false);

                        // This altEndpoint becomes the main endpoint, and the existing main endpoint becomes
                        // the first alt endpoint.
                        endpointFeature.AltEndpoints = endpointFeature.AltEndpoints.RemoveAt(i).Insert(0, mainEndpoint);
                        endpointFeature.Endpoint = altEndpoint;

                        return connection;
                    }
                    catch (Exception altEx)
                    {
                        exceptionList ??= new List<Exception> { ex };
                        exceptionList.Add(altEx);
                        // and keep trying
                    }
                }

                throw exceptionList is null ? ExceptionUtil.Throw(ex) : new AggregateException(exceptionList);
            }
        }
    }
}
