// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A connection pool manages a pool of client connections and is a client connection provider.</summary>
public sealed class ConnectionPool : IClientConnectionProvider, IAsyncDisposable
{
    // Connected connections that can be returned immediately
    private readonly Dictionary<Endpoint, ClientConnection> _activeConnections = new(EndpointComparer.ParameterLess);

    private readonly ILoggerFactory? _loggerFactory;
    private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<Endpoint, ClientConnection> _pendingConnections = new(EndpointComparer.ParameterLess);

    private readonly ConnectionPoolOptions _options;
    private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;

    private readonly object _mutex = new();

    private CancellationTokenSource? _shutdownCancelSource;
    private Task? _shutdownTask;

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

    /// <summary>An alias for <see cref="ShutdownAsync"/>, except this method returns a <see cref="ValueTask"/>.
    /// </summary>
    /// <returns>A value task constructed using the task returned by ShutdownAsync.</returns>
    public ValueTask DisposeAsync() => new(ShutdownAsync());

    /// <summary>Returns a client connection to one of the specified endpoints.</summary>
    /// <param name="endpoint">The first endpoint to try.</param>
    /// <param name="altEndpoints">The alternative endpoints.</param>
    /// <param name="cancel">The cancellation token.</param>
    public ValueTask<IClientConnection> GetClientConnectionAsync(
        Endpoint endpoint,
        IEnumerable<Endpoint> altEndpoints,
        CancellationToken cancel)
    {
        if (_options.PreferExistingConnection)
        {
            ClientConnection? connection = null;
            lock (_mutex)
            {
                connection = GetActiveConnection(endpoint);
                if (connection == null)
                {
                    foreach (Endpoint altEndpoint in altEndpoints)
                    {
                        connection = GetActiveConnection(altEndpoint);
                        if (connection != null)
                        {
                            break; // foreach
                        }
                    }
                }
            }
            if (connection != null)
            {
                return new(connection);
            }
        }

        return GetOrCreateAsync();

        ClientConnection? GetActiveConnection(Endpoint endpoint)
        {
            if (_activeConnections.TryGetValue(endpoint, out ClientConnection? connection))
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
        async ValueTask<IClientConnection> GetOrCreateAsync()
        {
            try
            {
                return await ConnectAsync(endpoint, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                List<Exception>? exceptionList = null;

                foreach (Endpoint altEndpoint in altEndpoints)
                {
                    try
                    {
                        return await ConnectAsync(altEndpoint, cancel).ConfigureAwait(false);
                    }
                    catch (Exception altEx)
                    {
                        if (exceptionList == null)
                        {
                            // we have at least 2 exceptions
                            exceptionList = new List<Exception> { ex, altEx };
                        }
                        else
                        {
                            exceptionList.Add(altEx);
                        }
                        // and keep trying
                    }
                }

                throw exceptionList == null ?
                    ExceptionUtil.Throw(ex) : new AggregateException(exceptionList);
            }
        }
    }

    /// <summary>Releases all resources used by this connection pool. This method can be called multiple times.
    /// </summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the destruction is complete.</returns>
    public async Task ShutdownAsync(CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            _shutdownCancelSource ??= new();
            _shutdownTask ??= PerformShutdownAsync();
        }

        // Cancel shutdown task if this call is canceled.
        using CancellationTokenRegistration _ = cancel.Register(() =>
        {
            try
            {
                _shutdownCancelSource!.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Expected if server shutdown completed already.
            }
        });

        await _shutdownTask.ConfigureAwait(false);

        async Task PerformShutdownAsync()
        {
            // Yield to ensure we don't hold the mutex while performing the shutdown.
            await Task.Yield();
            try
            {
                CancellationToken cancel = _shutdownCancelSource!.Token;

                // Shut down all connections managed by this pool.
                await Task.WhenAll(
                    _pendingConnections.Values.Concat(_activeConnections.Values).Select(connection =>
                        connection.ShutdownAsync("connection pool shutdown", cancel))).ConfigureAwait(false);
            }
            finally
            {
                _shutdownCancelSource!.Dispose();
            }
        }
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
            throw new FormatException($"cannot establish client connection using endpoint '{endpoint}'");
        }
    }

    /// <summary>Creates a connection and attempts to connect this connection unless there is an active or pending
    /// connection for the desired endpoint.</summary>
    /// <param name="endpoint">The endpoint of the server.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A connected connection.</returns>
    private async ValueTask<ClientConnection> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
    {
        ClientConnection? connection = null;
        bool created = false;

        lock (_mutex)
        {
            if (_shutdownTask != null)
            {
                throw new ObjectDisposedException($"{typeof(ConnectionPool)}");
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
                connection = new ClientConnection(
                    _options.ClientConnectionOptions with
                    {
                        RemoteEndpoint = endpoint
                    },
                    _loggerFactory,
                    _multiplexedClientTransport,
                    _simpleClientTransport);

                created = true;
                _pendingConnections.Add(endpoint, connection);
            }
        }

        try
        {
            // Make sure this connection/endpoint are actually usable.
            await connection.ConnectAsync(cancel).ConfigureAwait(false);
        }
        catch when (created)
        {
            lock (_mutex)
            {
                // _pendingConnections are immutable after shutdown
                if (_shutdownTask == null)
                {
                    bool removed = _pendingConnections.Remove(endpoint);
                    Debug.Assert(removed);
                }
            }
            throw;
        }

        if (created)
        {
            lock (_mutex)
            {
                if (_shutdownTask == null)
                {
                    // "move" from pending to active
                    bool removed = _pendingConnections.Remove(endpoint);
                    Debug.Assert(removed);
                    _activeConnections.Add(endpoint, connection);
                    connection.OnClose(RemoveFromActive); // schedule removal after addition
                }
                else
                {
                    // since connection is in _pendingConnections, ShutdownAsync is cleaning up this connection.
                    throw new ObjectDisposedException($"{typeof(ConnectionPool)}");
                }
            }
        }
        return connection;

        void RemoveFromActive(IConnection connection, Exception exception)
        {
            lock (_mutex)
            {
                // _activeConnections are immutable after shutdown
                if (_shutdownTask == null)
                {
                    var clientConnection = (ClientConnection)connection;
                    bool removed = _activeConnections.Remove(clientConnection.RemoteEndpoint);
                    Debug.Assert(removed);
                }
            }
        }
    }
}
