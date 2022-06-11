// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>A connection pool manages a pool of client connections and is a client connection provider.</summary>
public sealed class ConnectionPool : IClientConnectionProvider, IAsyncDisposable
{
    private readonly Dictionary<Endpoint, ClientConnection> _connections = new(EndpointComparer.ParameterLess);
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;
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
    /// <param name="clientConnectionOptions">The client connection options for connections created by this pool.</param>
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
                connection = GetCachedConnection(endpoint);
                if (connection == null)
                {
                    foreach (Endpoint altEndpoint in altEndpoints)
                    {
                        connection = GetCachedConnection(altEndpoint);
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

        return CreateConnectionAsync();

        async ValueTask<IClientConnection> CreateConnectionAsync()
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

        ClientConnection? GetCachedConnection(Endpoint endpoint)
        {
            if (_connections.TryGetValue(endpoint, out ClientConnection? connection))
            {
                CheckEndpoint(endpoint);
                return connection;
            }
            else
            {
                return null;
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

                // Shutdown all connections managed by this pool.
                await Task.WhenAll(
                    _connections.Values.Select(connection =>
                        connection.ShutdownAsync("connection pool shutdown", cancel))).ConfigureAwait(false);
            }
            finally
            {
                _shutdownCancelSource!.Dispose();
            }
        }
    }

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

    private async ValueTask<ClientConnection> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
    {
        ClientConnection? connection = null;
        lock (_mutex)
        {
            if (_shutdownTask != null)
            {
                throw new ObjectDisposedException($"{typeof(ConnectionPool)}");
            }

            // Check if there is a connected or just inserted connection that we can use according to the endpoint
            // settings.
            if (_connections.TryGetValue(endpoint, out connection))
            {
                CheckEndpoint(endpoint);
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

                connection.OnClose(RemoveOnClose);
                _connections.Add(endpoint, connection);
            }
        }

        // We call connect to make sure this connection/endpoint are actually usable.
        await connection.ConnectAsync(cancel).ConfigureAwait(false);
        return connection;

        void RemoveOnClose(IConnection connection, Exception? exception)
        {
            var clientConnection = (ClientConnection)connection;

            lock (_mutex)
            {
                // _connections is immutable after shutdown
                if (_shutdownTask == null)
                {
                    _ = _connections.Remove(clientConnection.RemoteEndpoint);
                }
            }
        }
    }
}
