// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A connection pool manages a pool of client connections and is a connection provider for the
    /// <see cref="Interceptors.Binder"/> interceptor.</summary>
    public sealed partial class ConnectionPool : IConnectionProvider, IAsyncDisposable
    {
        /// <summary>The connection options.</summary>
        public ClientConnectionOptions? ConnectionOptions { get; set; }

        /// <summary>Gets or sets the logger factory of this connection pool. When null, the connection pool creates
        /// its logger using <see cref="Runtime.DefaultLoggerFactory"/>.</summary>
        /// <value>The logger factory of this connection pool.</value>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set
            {
                _loggerFactory = value;
                _logger = null; // clears existing logger, if there is one
            }
        }

        /// <summary>Indicates whether or not <see cref="GetConnectionAsync"/> prefers returning an existing connection
        /// over creating a new one.</summary>
        /// <value>When <c>true</c>, GetConnectionAsync first iterates over all endpoints (in order) to look for an
        /// existing active connection; if it cannot find such a connection, it creates one by iterating again over
        /// the endpoints. When <c>false</c>, GetConnectionAsync iterates over the endpoints only once to retrieve or
        /// create an active connection. The default value is <c>true</c>.</value>
        public bool PreferExistingConnection { get; set; } = true;

        internal CancellationToken CancellationToken
        {
            get
            {
                try
                {
                    return _cancellationTokenSource.Token;
                }
                catch (ObjectDisposedException ex)
                {
                    throw new ConnectionPoolDisposedException(ex);
                }
            }
        }

        internal ILogger Logger => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");

        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private Task? _shutdownTask;

        private readonly object _mutex = new();

        private readonly Dictionary<Endpoint, List<Connection>> _clientConnections =
           new(EndpointComparer.Equivalent);
        private readonly Dictionary<Endpoint, Task<Connection>> _pendingClientConnections =
            new(EndpointComparer.Equivalent);

        /// <summary>An alias for <see cref="ShutdownAsync"/>, except this method returns a <see cref="ValueTask"/>.
        /// </summary>
        /// <returns>A value task constructed using the task returned by ShutdownAsync.</returns>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        /// <summary>Returns a connection to one of the specified endpoints. The behavior of this method depends on
        /// <see cref="PreferExistingConnection"/>.</summary>
        /// <param name="endpoint">The first endpoint to try.</param>
        /// <param name="altEndpoints">The alternative endpoints.</param>
        /// <param name="cancel">The cancellation token.</param>
        public ValueTask<Connection> GetConnectionAsync(
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints,
            CancellationToken cancel)
        {
            if (PreferExistingConnection)
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
                ClientConnectionOptions connectionOptions = ConnectionOptions ?? ClientConnectionOptions.Default;
                try
                {
                    return await ConnectAsync(endpoint, connectionOptions, cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    List<Exception>? exceptionList = null;

                    foreach (Endpoint altEndpoint in altEndpoints)
                    {
                        try
                        {
                            return await ConnectAsync(altEndpoint, connectionOptions, cancel).ConfigureAwait(false);
                        }
                        catch (Exception altEx)
                        {
                            exceptionList ??= new List<Exception> { ex };
                            exceptionList.Add(altEx);
                            // and keep trying
                        }
                    }
                    throw exceptionList == null ? ExceptionUtil.Throw(ex) : new AggregateException(exceptionList);
                }
            }

            Connection? GetCachedConnection(Endpoint endpoint) =>
                _clientConnections.TryGetValue(endpoint, out List<Connection>? connections) &&
                connections.FirstOrDefault(connection => connection.IsActive) is Connection connection ?
                    connection : null;
        }

        /// <summary>Releases all resources used by this connection pool. This method can be called multiple times.
        /// </summary>
        /// <returns>A task that completes when the destruction is complete.</returns>
        // TODO: add cancellation token, use Yield
        public Task ShutdownAsync()
        {
            lock (_mutex)
            {
                _shutdownTask ??= PerformShutdownAsync();
                return _shutdownTask;
            }

            async Task PerformShutdownAsync()
            {
                // Cancel operations that are waiting and using the connection pool cancellation token
                _cancellationTokenSource.Cancel();

                // Shutdown and destroy all the incoming and outgoing IceRPC connections and wait for the connections
                // to be finished.
                IEnumerable<Task> closeTasks =
                    _clientConnections.Values.SelectMany(connections => connections).Select(
                        connection => connection.ShutdownAsync("connection pool shutdown"));

                await Task.WhenAll(closeTasks).ConfigureAwait(false);

                foreach (Task<Connection> connect in _pendingClientConnections.Values)
                {
                    try
                    {
                        Connection connection = await connect.ConfigureAwait(false);
                        await connection.ShutdownAsync("connection pool shutdown").ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                // Ensure all the client connections were removed
                Debug.Assert(_clientConnections.Count == 0);
                _cancellationTokenSource.Dispose();
            }
        }

        private async ValueTask<Connection> ConnectAsync(
            Endpoint endpoint,
            ClientConnectionOptions options,
            CancellationToken cancel)
        {
            Task<Connection>? connectTask;
            Connection? connection;
            do
            {
                lock (_mutex)
                {
                    if (_shutdownTask != null)
                    {
                        throw new ConnectionPoolDisposedException();
                    }

                    // Check if there is an active connection that we can use according to the endpoint settings.
                    if (_clientConnections.TryGetValue(endpoint, out List<Connection>? connections))
                    {
                        connection = connections.FirstOrDefault(connection => connection.IsActive);

                        if (connection != null)
                        {
                            return connection;
                        }
                    }

                    // If we didn't find an active connection check if there is a pending connect task for the same
                    // endpoint.
                    if (!_pendingClientConnections.TryGetValue(endpoint, out connectTask))
                    {
                        connectTask = PerformConnectAsync(endpoint, options);
                        if (!connectTask.IsCompleted)
                        {
                            // If the task didn't complete synchronously we add it to the pending map
                            // and it will be removed once PerformConnectAsync completes.
                            _pendingClientConnections[endpoint] = connectTask;
                        }
                    }
                }

                connection = await connectTask.IceWaitAsync(cancel).ConfigureAwait(false);
            }
            while (connection == null);
            return connection;

            async Task<Connection> PerformConnectAsync(Endpoint endpoint, ClientConnectionOptions options)
            {
                Debug.Assert(options.ConnectTimeout > TimeSpan.Zero);
                // Use the connect timeout and the cancellation token for the cancellation.
                using var source = new CancellationTokenSource(options.ConnectTimeout);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    source.Token,
                    CancellationToken);
                CancellationToken cancel = linkedSource.Token;

                try
                {
                    var connection = new Connection
                    {
                        RemoteEndpoint = endpoint,
                        Logger = Logger,
                        Options = options
                    };

                    // Connect the connection (handshake, protocol initialization, ...)
                    await connection.ConnectAsync(cancel).ConfigureAwait(false);

                    lock (_mutex)
                    {
                        if (_shutdownTask != null)
                        {
                            // If the connection pool has been disposed return the connection here and avoid adding the
                            // connection to the client connections map, the connection will be disposed from the
                            // pending connections map.
                            return connection;
                        }

                        if (!_clientConnections.TryGetValue(endpoint, out List<Connection>? list))
                        {
                            list = new List<Connection>();
                            _clientConnections[endpoint] = list;
                        }
                        list.Add(connection);
                    }
                    // Set the callback used to remove the connection from the factory.
                    connection.Remove = connection => Remove(connection);
                    return connection;
                }
                catch (OperationCanceledException) when (source.IsCancellationRequested)
                {
                    throw new ConnectTimeoutException();
                }
                finally
                {
                    lock (_mutex)
                    {
                        // Don't modify the pending connections map after the connection pool has been disposed.
                        if (_shutdownTask == null)
                        {
                            _pendingClientConnections.Remove(endpoint);
                        }
                    }
                }
            }
        }

        private void Remove(Connection connection)
        {
            lock (_mutex)
            {
                List<Connection> list = _clientConnections[connection.RemoteEndpoint!];
                list.Remove(connection);
                if (list.Count == 0)
                {
                    _clientConnections.Remove(connection.RemoteEndpoint!);
                }
            }
        }
    }
}
