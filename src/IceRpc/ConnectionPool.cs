// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>A connection pool manages a pool of client connections and is a client connection provider.</summary>
    public sealed partial class ConnectionPool : IClientConnectionProvider, IAsyncDisposable
    {
        private readonly ConnectionPoolOptions _connectionPoolOptions;
        private readonly Dictionary<Endpoint, List<ClientConnection>> _connections = new(EndpointComparer.ParameterLess);
        private readonly ILoggerFactory? _loggerFactory;
        private readonly IClientTransport<IMultiplexedNetworkConnection>? _multiplexedClientTransport;
        private readonly IClientTransport<ISimpleNetworkConnection>? _simpleClientTransport;

        private readonly object _mutex = new();

        private CancellationTokenSource? _shutdownCancelSource;
        private Task? _shutdownTask;

        /// <summary>Constructs a connection pool.</summary>
        /// <param name="connectionPoolOptions">The connection pool options.</param>
        /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
        /// </param>
        /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol
        /// connections.</param>
        /// <param name="simpleClientTransport">The simple transport used to create ice protocol connections.</param>
        public ConnectionPool(
            ConnectionPoolOptions connectionPoolOptions,
            ILoggerFactory? loggerFactory = null,
            IClientTransport<IMultiplexedNetworkConnection>? multiplexedClientTransport = null,
            IClientTransport<ISimpleNetworkConnection>? simpleClientTransport = null)
        {
            _connectionPoolOptions = connectionPoolOptions;
            _loggerFactory = loggerFactory;
            _multiplexedClientTransport = multiplexedClientTransport;
            _simpleClientTransport = simpleClientTransport;
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
            if (_connectionPoolOptions.PreferExistingConnection)
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
                        catch (UnknownTransportException)
                        {
                            // ignored, continue for loop
                        }
                        catch (Exception altEx)
                        {
                            if (exceptionList == null)
                            {
                                if (ex is UnknownTransportException)
                                {
                                    // keep in ex the first exception that is not an UnknownTransportException
                                    ex = altEx;
                                }
                                else
                                {
                                    // we have at least 2 exceptions that are not UnknownTransportException
                                    exceptionList = new List<Exception> { ex, altEx };
                                }
                            }
                            else
                            {
                                exceptionList.Add(altEx);
                            }
                            // and keep trying
                        }
                    }

                    throw exceptionList == null ?
                        (ex is UnknownTransportException ? new NoEndpointException() : ExceptionUtil.Throw(ex)) :
                        new AggregateException(exceptionList);
                }
            }

            ClientConnection? GetCachedConnection(Endpoint endpoint) =>
                _connections.TryGetValue(endpoint, out List<ClientConnection>? connections) &&
                connections.FirstOrDefault(
                    connection =>
                        connection.HasCompatibleParams(endpoint) &&
                        connection.State <= ConnectionState.Active) is ClientConnection connection ? connection : null;
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
                    await Task.WhenAll(_connections.Values.SelectMany(connections => connections).Select(
                        connection => connection.ShutdownAsync(
                            "connection pool shutdown",
                            cancel))).ConfigureAwait(false);
                }
                finally
                {
                    _shutdownCancelSource!.Dispose();
                }
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

                // Check if there is an active or pending connection that we can use according to the endpoint
                // settings.
                if (_connections.TryGetValue(endpoint, out List<ClientConnection>? connections))
                {
                    connection = connections.FirstOrDefault(connection => connection.State <= ConnectionState.Active);
                }

                if (connection == null)
                {
                    // Connections from the connection pool are not resumable.
                    connection = new ClientConnection(
                        _connectionPoolOptions.ClientConnectionOptions with
                        {
                            OnClose = RemoveOnClose + _connectionPoolOptions.ClientConnectionOptions.OnClose,
                            RemoteEndpoint = endpoint
                        },
                        _loggerFactory,
                        _multiplexedClientTransport,
                        _simpleClientTransport);

                    if (!_connections.TryGetValue(endpoint, out connections))
                    {
                        connections = new List<ClientConnection>();
                        _connections[endpoint] = connections;
                    }
                    connections.Add(connection);
                }
            }
            await connection.ConnectAsync(cancel).ConfigureAwait(false);
            return connection;

            void RemoveOnClose(Connection connection, Exception _)
            {
                var clientConnection = (ClientConnection)connection;

                lock (_mutex)
                {
                    // _connections is immutable after shutdown
                    if (_shutdownTask == null)
                    {
                        List<ClientConnection> list = _connections[clientConnection.RemoteEndpoint];
                        list.Remove(clientConnection);
                        if (list.Count == 0)
                        {
                            _connections.Remove(clientConnection.RemoteEndpoint);
                        }
                    }
                }
            }
        }
    }
}
