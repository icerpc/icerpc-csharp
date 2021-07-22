// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        /// <summary>The <see cref="IClientTransport"/> used by the connections created by this pool.
        /// </summary>
        public IClientTransport ClientTransport { get; init; } = Connection.DefaultClientTransport;

        /// <summary>The connection options.</summary>
        public ClientConnectionOptions? ConnectionOptions { get; set; }

        /// <summary>Gets or sets the logger factory of this connection pool. When null, the connection pool creates
        /// its logger using <see cref="NullLoggerFactory.Instance"/>.</summary>
        /// <value>The logger factory of this connection pool.</value>
        public ILoggerFactory? LoggerFactory { get; init; }

        /// <summary>Indicates whether or not <see cref="GetConnectionAsync"/> prefers returning an existing connection
        /// over creating a new one.</summary>
        /// <value>When <c>true</c>, GetConnectionAsync first iterates over all endpoints (in order) to look for an
        /// existing active connection; if it cannot find such a connection, it creates one by iterating again over
        /// the endpoints. When <c>false</c>, GetConnectionAsync iterates over the endpoints only once to retrieve or
        /// create an active connection. The default value is <c>true</c>.</value>
        public bool PreferExistingConnection { get; set; } = true;

        private readonly Dictionary<Endpoint, List<Connection>> _connections = new(EndpointComparer.Equivalent);
        private readonly object _mutex = new();
        private CancellationTokenSource? _shutdownCancelSource;
        private Task? _shutdownTask;

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
            IEnumerable<Endpoint> endpoints = altEndpoints.Prepend(endpoint);

            if (PreferExistingConnection)
            {
                lock (_mutex)
                {
                    foreach (Endpoint e in endpoints)
                    {
                        if (GetCachedConnection(e) is Connection connection)
                        {
                            return new ValueTask<Connection>(connection);
                        }
                    }
                }
            }

            return CreateConnectionAsync();

            async ValueTask<Connection> CreateConnectionAsync()
            {
                ClientConnectionOptions connectionOptions = ConnectionOptions ?? ClientConnectionOptions.Default;
                List<Exception>? exceptionList = null;

                foreach (Endpoint e in endpoints)
                {
                    try
                    {
                        return await ConnectAsync(e, connectionOptions, cancel).ConfigureAwait(false);
                    }
                    catch (UnknownTransportException)
                    {
                        // ignored, continue for loop
                    }
                    catch (Exception ex)
                    {
                        exceptionList ??= new List<Exception>();
                        exceptionList.Add(ex);
                        // and keep trying
                    }
                }

                if (exceptionList == null)
                {
                    throw new NoEndpointException();
                }
                else if (exceptionList.Count == 1)
                {
                    throw ExceptionUtil.Throw(exceptionList[0]);
                }
                else
                {
                    throw new AggregateException(exceptionList);
                }
            }

            Connection? GetCachedConnection(Endpoint endpoint) =>
                _connections.TryGetValue(endpoint, out List<Connection>? connections) &&
                connections.FirstOrDefault(
                    connection => connection.State == ConnectionState.Active) is Connection connection ?
                        connection : null;
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

        private async ValueTask<Connection> ConnectAsync(
            Endpoint endpoint,
            ClientConnectionOptions options,
            CancellationToken cancel)
        {
            Task? connectTask;
            Connection? connection = null;
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ConnectionPool).FullName}");
                }

                // Check if there is an active or pending connection that we can use according to the endpoint
                // settings.
                if (_connections.TryGetValue(endpoint, out List<Connection>? connections))
                {
                    connection = connections.FirstOrDefault(connection => connection.State <= ConnectionState.Active);
                }

                if (connection != null)
                {
                    if (connection.State == ConnectionState.Active)
                    {
                        return connection;
                    }
                    connectTask = connection.ConnectAsync(default);
                }
                else
                {
                    Debug.Assert(options.ConnectTimeout > TimeSpan.Zero);
                    // Dispose objects before losing scope, the connection is disposed from ShutdownAsync.
#pragma warning disable CA2000
                    connection = new Connection
                    {
                        RemoteEndpoint = endpoint,
                        LoggerFactory = LoggerFactory,
                        ClientTransport = ClientTransport,
                        Options = options
                    };
#pragma warning restore CA2000
                    if (!_connections.TryGetValue(endpoint, out connections))
                    {
                        connections = new List<Connection>();
                        _connections[endpoint] = connections;
                    }
                    connections.Add(connection);
                    // Set the callback used to remove the connection from the pool.
                    connection.Remove = connection => Remove(connection);
                    connectTask = PerformConnectAsync(connection);
                }
            }
            await connectTask.WaitAsync(cancel).ConfigureAwait(false);

            return connection;

            async Task PerformConnectAsync(Connection connection)
            {
                // Use the connect timeout and the cancellation token for the cancellation.
                using var source = new CancellationTokenSource(options.ConnectTimeout);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token, cancel);

                try
                {
                    // Connect the connection (handshake, protocol initialization, ...)
                    await connection.ConnectAsync(linkedSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (source.IsCancellationRequested)
                {
                    throw new ConnectTimeoutException();
                }
            }
        }

        private void Remove(Connection connection)
        {
            lock (_mutex)
            {
                // _connections is immutable after shutdown
                if (_shutdownTask == null)
                {
                    List<Connection> list = _connections[connection.RemoteEndpoint!];
                    list.Remove(connection);
                    if (list.Count == 0)
                    {
                        _connections.Remove(connection.RemoteEndpoint!);
                    }
                }
            }
        }
    }
}
