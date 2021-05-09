// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A connection pool manages a pool of outgoing connections. When used as an invoker and pipeline, it
    /// installs automatically the <see cref="Interceptors.Retry"/>, <see cref="Interceptors.Coloc"/> and
    /// <see cref="Interceptors.Binder"/> interceptors before all other interceptors. Retry is installed with max
    /// attempts set to 5 and this connection pool's logger factory; and Binder is installed with this connection pool
    /// as connection provider.</summary>
    // TODO: rename to ConnectionPool
    public sealed partial class Communicator : Pipeline, IConnectionProvider, IAsyncDisposable
    {
        /// <summary>The connection options.</summary>
        public OutgoingConnectionOptions? ConnectionOptions { get; set; }

        /// <summary>Indicates whether or not this connection pool can be used as an invoker.</summary>
        /// <value>When <c>true</c> (the default) this connection pool can be used an invoker and implements
        /// <see cref="IInvoker.InvokeAsync"/> using its base class. When <c>false</c>, calling InvokeAsync on this
        /// connection pool throws <see cref="InvalidOperationException"/>.</value>
        /// <remarks>Setting this value to false prevents you from using this connection pool as an invoker by
        /// accident when you want to use it only as a connection provider for the <see cref="Interceptors.Binder"/>
        /// interceptor.</remarks>
        public bool IsInvoker { get; set; } = true;

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
                    throw new CommunicatorDisposedException(ex);
                }
            }
        }

        /// <summary>The default logger for this communicator.</summary>
        internal ILogger Logger => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");

        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private Task? _shutdownTask;

        private readonly object _mutex = new();

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
                                break; // for
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
                OutgoingConnectionOptions connectionOptions = ConnectionOptions ?? OutgoingConnectionOptions.Default;
                Connection? connection = null;

                // TODO: add back OrderEndpointsByTransportFailures?
                // Do we actually want to return Connected connections here or just created is better?

                try
                {
                    connection = await ConnectAsync(endpoint, connectionOptions, cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    List<Exception>? exceptionList = null;

                    foreach (Endpoint altEndpoint in altEndpoints)
                    {
                        try
                        {
                            connection =
                                await ConnectAsync(altEndpoint, connectionOptions, cancel).ConfigureAwait(false);
                            break;
                        }
                        catch (Exception altEx)
                        {
                            exceptionList ??= new List<Exception> { ex };
                            exceptionList.Add(altEx);
                            // and keep trying
                        }
                    }

                    if (connection == null)
                    {
                        throw exceptionList == null ? ex : new AggregateException(exceptionList);
                    }
                }
                return connection;
            }

            Connection? GetCachedConnection(Endpoint endpoint) =>
                _outgoingConnections.TryGetValue(endpoint, out LinkedList<Connection>? connections) &&
                connections.FirstOrDefault(connection => connection.IsActive) is Connection connection ?
                    connection : null;
        }

        /// <summary>Releases all resources used by this communicator. This method can be called multiple times.
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
                // Cancel operations that are waiting and using the communicator's cancellation token
                _cancellationTokenSource.Cancel();

                // Shutdown and destroy all the incoming and outgoing Ice connections and wait for the connections to be
                // finished.
                IEnumerable<Task> closeTasks =
                    _outgoingConnections.Values.SelectMany(connections => connections).Select(
                        connection => connection.ShutdownAsync("connection pool shutdown"));

                await Task.WhenAll(closeTasks).ConfigureAwait(false);

                foreach (Task<Connection> connect in _pendingOutgoingConnections.Values)
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

                // Ensure all the outgoing connections were removed
                Debug.Assert(_outgoingConnections.Count == 0);
                _cancellationTokenSource.Dispose();
            }
        }

        protected override IInvoker CreateInvoker(IInvoker lastInvoker)
        {
            if (IsInvoker)
            {
                IInvoker pipeline = base.CreateInvoker(lastInvoker);

                // Add default interceptors in reverse order of execution.
                pipeline = Interceptors.Binder(this)(pipeline);
                pipeline = Interceptors.Coloc(pipeline);
                pipeline = Interceptors.Retry(maxAttempts: 5, loggerFactory: LoggerFactory)(pipeline);

                return pipeline;
            }
            else
            {
                throw new InvalidOperationException("this connection pool is not usable as an invoker");
            }
        }
    }
}
