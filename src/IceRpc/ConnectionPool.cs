// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc
{
    /// <summary>A connection pool manages a pool of client connections and is a connection provider for the
    /// <see cref="BinderInterceptor"/> interceptor.</summary>
    public sealed partial class ConnectionPool : IConnectionProvider, IAsyncDisposable
    {
        private readonly ConnectionOptions? _connectionOptions;
        private readonly Dictionary<Endpoint, List<Connection>> _connections = new(EndpointComparer.ParameterLess);
        private readonly object _mutex = new();
        private readonly bool _preferExistingConnection;
        private CancellationTokenSource? _shutdownCancelSource;
        private Task? _shutdownTask;

        /// <summary>Constructs a connection pool.</summary>
        /// <param name="connectionOptions">The connection options. Its <see cref="ConnectionOptions.RemoteEndpoint"/>
        /// property is ignored.</param>
        /// <param name="preferExistingConnection">Configures whether or not <see cref="GetConnectionAsync"/> prefers
        /// returning an existing connection over creating a new one. When <c>true</c>, GetConnectionAsync first
        /// iterates over all endpoints (in order) to look for an existing compatible active connection; if it cannot
        /// find such a connection, it creates one by iterating again over the endpoints. When <c>false</c>,
        /// GetConnectionAsync iterates over the endpoints only once to retrieve or create an active connection. The
        /// default value is <c>true</c>.</param>
        public ConnectionPool(ConnectionOptions? connectionOptions = null, bool preferExistingConnection = true)
        {
            _connectionOptions = connectionOptions;
            _preferExistingConnection = preferExistingConnection;
        }

        /// <summary>An alias for <see cref="ShutdownAsync"/>, except this method returns a <see cref="ValueTask"/>.
        /// </summary>
        /// <returns>A value task constructed using the task returned by ShutdownAsync.</returns>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        /// <summary>Returns a connection to one of the specified endpoints.</summary>
        /// <param name="endpoint">The first endpoint to try.</param>
        /// <param name="altEndpoints">The alternative endpoints.</param>
        /// <param name="cancel">The cancellation token.</param>
        public ValueTask<Connection> GetConnectionAsync(
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints,
            CancellationToken cancel)
        {
            if (_preferExistingConnection)
            {
                Connection? connection = null;
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

            async ValueTask<Connection> CreateConnectionAsync()
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

            Connection? GetCachedConnection(Endpoint endpoint) =>
                _connections.TryGetValue(endpoint, out List<Connection>? connections) &&
                connections.FirstOrDefault(
                    connection =>
                        connection.HasCompatibleParams(endpoint) &&
                        connection.State <= ConnectionState.Active) is Connection connection ? connection : null;
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

        private async ValueTask<Connection> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            Connection? connection = null;
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ConnectionPool)}");
                }

                // Check if there is an active or pending connection that we can use according to the endpoint
                // settings.
                if (_connections.TryGetValue(endpoint, out List<Connection>? connections))
                {
                    connection = connections.FirstOrDefault(connection => connection.State <= ConnectionState.Active);
                }

                if (connection == null)
                {
                    // Connections from the connection pool are not resumable.
                    connection = new Connection(
                        _connectionOptions is ConnectionOptions connectionOptions ?
                            connectionOptions with { RemoteEndpoint = endpoint } :
                            new ConnectionOptions { RemoteEndpoint = endpoint });

                    if (!_connections.TryGetValue(endpoint, out connections))
                    {
                        connections = new List<Connection>();
                        _connections[endpoint] = connections;
                    }
                    connections.Add(connection);

                    // Set the callback used to remove the connection from the pool. This can throw if the connection is
                    // closed but it's not possible here since we've just constructed the connection.
                    connection.Closed += (sender, state) => Remove(endpoint, connection);
                }
            }
            await connection.ConnectAsync(cancel).ConfigureAwait(false);
            return connection;
        }

        private void Remove(Endpoint endpoint, Connection connection)
        {
            lock (_mutex)
            {
                // _connections is immutable after shutdown
                if (_shutdownTask == null)
                {
                    List<Connection> list = _connections[endpoint];
                    list.Remove(connection);
                    if (list.Count == 0)
                    {
                        _connections.Remove(endpoint);
                    }
                }
            }
        }
    }
}
