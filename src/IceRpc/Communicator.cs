// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An invoker that manages a pool of outgoing connections and supports the installation of interceptors.
    /// </summary>
    // TODO: rename to ConnectionPool
    public sealed partial class Communicator : IConnectionProvider, IInvoker, IAsyncDisposable
    {
        /// <summary>The connection options.</summary>
        public OutgoingConnectionOptions? ConnectionOptions { get; set; }

        /// <summary>Configures the installation of the default interceptors for this connection pool.</summary>
        /// <value>When <c>true</c> (the default), the connection pool installs the Retry, Coloc and Binder interceptors
        /// before any interceptor installed by calling <see cref="Use"/>. The Retry interceptor is configured with
        /// 5 attempts and this connection pool's logger factory. The Binder interceptor is configured with this
        /// connection pool. When <c>false</c>, no interceptor is installed automatically.</value>
        public bool InstallDefaultInterceptors { get; set; } = true;

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

        /// <summary>Indicates whether or not this connection pool prefers using an existing connection over creating
        /// a new one when supplying a connection to an outgoing request.</summary>
        /// <value>When <c>true</c>, the connection pool first iterates over all endpoints (in order) to look for an
        /// existing active connection; if it cannot find such a connection, it creates one by iterating again over
        /// the endpoints. When <c>false</c>, the connection pool iterates over the endpoints only once to retrieve or
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

        private IInvoker? _invoker;
        private ImmutableList<Func<IInvoker, IInvoker>> _interceptorList =
            ImmutableList<Func<IInvoker, IInvoker>>.Empty;

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

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            (_invoker ??= CreateInvokerPipeline()).InvokeAsync(request, cancel);

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

        /// <summary>Installs one or more interceptors.</summary>
        /// <param name="interceptor">One or more interceptors.</param>
        /// <exception name="InvalidOperationException">Thrown if this method is called after the first call to
        /// <see cref="InvokeAsync"/>.</exception>
        public void Use(params Func<IInvoker, IInvoker>[] interceptor)
        {
            if (_invoker != null)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _interceptorList = _interceptorList.AddRange(interceptor);
        }

        private IInvoker CreateInvokerPipeline()
        {
            IInvoker pipeline = new InlineInvoker((request, cancel) =>
                request.Connection is Connection connection ? TempConnectionInvokeAsync(connection, request, cancel) :
                    throw new ArgumentNullException($"{nameof(request.Connection)} is null", nameof(request)));

            IEnumerable<Func<IInvoker, IInvoker>> interceptorEnumerable = _interceptorList;
            foreach (Func<IInvoker, IInvoker> interceptor in interceptorEnumerable.Reverse())
            {
                pipeline = interceptor(pipeline);
            }

            if (InstallDefaultInterceptors)
            {
                // Add default interceptors in reverse order of execution.

                pipeline = Interceptor.Binder(this)(pipeline);
                pipeline = Interceptor.Coloc(pipeline);
                pipeline = Interceptor.Retry(maxAttempts: 5, loggerFactory: LoggerFactory)(pipeline);
            }

            return pipeline;

            static async Task<IncomingResponse> TempConnectionInvokeAsync(
                Connection connection,
                OutgoingRequest request,
                CancellationToken cancel)
            {
                if (Activity.Current != null && Activity.Current.Id != null)
                {
                    request.WriteActivityContext(Activity.Current);
                }

                SocketStream? stream = null;
                try
                {
                    using IDisposable? socketScope = connection.StartScope();

                    // Create the outgoing stream.
                    stream = connection.CreateStream(!request.IsOneway);

                    // Send the request and wait for the sending to complete.
                    await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                    // TODO: move this set to stream.SendRequestFrameAsync
                    request.IsSent = true;

                    using IDisposable? streamSocket = stream.StartScope();
                    connection.Logger.LogSentRequest(request);

                    // The request is sent, notify the progress callback.
                    // TODO: Get rid of the sentSynchronously parameter which is always false now?
                    if (request.Progress is IProgress<bool> progress)
                    {
                        progress.Report(false);
                        request.Progress = null; // Only call the progress callback once (TODO: revisit this?)
                    }

                    // Wait for the reception of the response.
                    IncomingResponse response = request.IsOneway ?
                        new IncomingResponse(connection, request.PayloadEncoding) :
                        await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);
                    response.Connection = connection;

                    if (!request.IsOneway)
                    {
                        connection.Logger.LogReceivedResponse(response);
                    }
                    return response;
                }
                finally
                {
                    // Release one ref-count
                    stream?.Release();
                }
            }
        }
    }
}
