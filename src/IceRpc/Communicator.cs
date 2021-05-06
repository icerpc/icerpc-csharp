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
    public sealed partial class Communicator : IInvoker, IAsyncDisposable
    {
        /// <summary>Indicates whether or not connection pool caches an active connection found or created during
        /// <see cref="InvokeAsync"/> in the proxy that is sending the request.</summary>
        /// <value>True when the connection pool caches the connections in the proxies; otherwise, false. The default
        /// value is true.</value>
        public bool CacheConnection { get; set; } = true;

        /// <summary>The connection options.</summary>
        public OutgoingConnectionOptions? ConnectionOptions { get; set; }

        /// <summary>Gets the maximum number of invocation attempts made to send a request including the original
        /// invocation. It must be a number greater than 0.</summary>
        public int InvocationMaxAttempts { get; set; } = 5; // TODO: > 0 and <= 5

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
        /// a new one during binding.</summary>
        /// <value>When <c>true</c>, the connection pool first iterates over all endpoints (in order) to look for an
        /// existing active connection; if it cannot find such a connection, it creates one by iterating again over
        /// the endpoints. When <c>false</c>, the connection pool iterates over the endpoints only once to retrieve or
        /// create an active connection. The default value is <c>true</c>.</value>
        public bool PreferExistingConnection { get; set; } = true;

        public int RetryBufferMaxSize { get; set; } = 1024 * 1024 * 100;
        public int RetryRequestMaxSize { get; set; } = 1024 * 1024;

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
        private ImmutableList<Func<IInvoker, IInvoker>> _invokerInterceptorList =
            ImmutableList<Func<IInvoker, IInvoker>>.Empty;

        private ILocationResolver? _locationResolver;
        private ImmutableList<Func<ILocationResolver, ILocationResolver>> _locationResolverInterceptorList =
            ImmutableList<Func<ILocationResolver, ILocationResolver>>.Empty;

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private Task? _shutdownTask;

        private readonly object _mutex = new();

        private int _retryBufferSize;

        public Communicator()
        {
        }

        /// <summary>An alias for <see cref="ShutdownAsync"/>, except this method returns a <see cref="ValueTask"/>.
        /// </summary>
        /// <returns>A value task constructed using the task returned by ShutdownAsync.</returns>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            _locationResolver ??= CreateLocationResolverPipeline();
            _invoker ??= CreateInvokerPipeline();

            // If the request size is greater than Ice.RetryRequestSizeMax or the size of the request
            // would increase the buffer retry size beyond Ice.RetryBufferSizeMax we release the request
            // after it was sent to avoid holding too much memory and we wont retry in case of a failure.

            // TODO: this "request size" is now just the payload size. Should we rename the property to
            // RetryRequestPayloadMaxSize?

            int requestSize = request.PayloadSize;
            bool releaseRequestAfterSent =
                requestSize > RetryRequestMaxSize || !IncRetryBufferSize(requestSize);

            try
            {
                if (Activity.Current != null && Activity.Current.Id != null)
                {
                    request.WriteActivityContext(Activity.Current);
                }

                ILogger logger = Logger;
                int attempt = 1;
                List<Endpoint>? excludedEndpoints = null;
                IncomingResponse? response = null;
                Exception? exception = null;

                bool tryAgain = false;

                ServicePrx proxy = request.Proxy.Impl;
                Endpoint? endpoint = request.Proxy.Endpoint;
                IEnumerable<Endpoint> altEndpoints = request.Proxy.AltEndpoints;

                do
                {
                    while (request.Connection == null)
                    {
                        if (endpoint?.Transport == Transport.Loc)
                        {
                            (endpoint, altEndpoints) =
                                 await _locationResolver.ResolveAsync(endpoint!,
                                                                      refreshCache: tryAgain,
                                                                      cancel).ConfigureAwait(false);
                        }

                        if (endpoint != null)
                        {
                            // It's critical to perform the coloc conversion before filtering the endpoints and calling
                            // BindAsync because excludedEndpoints are populated using
                            // request.Connection.RemoteEndpoint.
                            endpoint = endpoint.ToColocEndpoint() ?? endpoint;
                            altEndpoints = altEndpoints.Select(e => e.ToColocEndpoint() ?? e);

                            if (!IsUsable(endpoint, request.IsOneway))
                            {
                                endpoint = null;
                            }
                            altEndpoints = altEndpoints.Where(e => IsUsable(e, request.IsOneway));

                            // Filter-out excluded endpoints before calling BindAsync
                            if (excludedEndpoints != null)
                            {
                                if (endpoint != null && excludedEndpoints.Contains(endpoint))
                                {
                                    endpoint = null;
                                }
                                altEndpoints = altEndpoints.Except(excludedEndpoints);
                            }

                            if (endpoint == null && altEndpoints.Any())
                            {
                                endpoint = altEndpoints.First();
                                altEndpoints = altEndpoints.Skip(1);
                            }
                        }

                        if (endpoint == null)
                        {
                            // We can't get an endpoint: either the proxy has no usable endpoint to start with, or all
                            // the endpoints were tried and did not work.
                            return response ?? throw exception ?? new NoEndpointException(request.Proxy.ToString()!);
                        }

                        try
                        {
                            request.Connection = await BindAsync(endpoint, altEndpoints, cancel).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (tryAgain)
                            {
                                // We failed to establish a connection to any of the just refreshed endpoints. Retrying
                                // more is pointless.
                                return response ?? throw exception!;
                            }
                            else
                            {
                                exception = ex;
                                excludedEndpoints ??= new();
                                excludedEndpoints.Add(endpoint!);
                                excludedEndpoints.AddRange(altEndpoints);

                                // We don't consider this failure to obtain a connection as an attempt.
                                // TODO: logging

                                tryAgain = true;
                            }
                        }
                    }

                    Debug.Assert(request.Connection != null);
                    if (CacheConnection)
                    {
                        request.Proxy.Connection = request.Connection;
                    }

                    try
                    {
                        request.IsSent = false;
                        response = await _invoker.InvokeAsync(request, cancel).ConfigureAwait(false);

                        if (response.ResultType == ResultType.Success)
                        {
                            return response;
                        }
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }

                    // Compute retry policy based on the exception or response retry policy, whether or not the
                    // connection is established or the request sent and idempotent
                    Debug.Assert(response != null || exception != null);
                    RetryPolicy retryPolicy = response?.GetRetryPolicy(proxy) ??
                        exception!.GetRetryPolicy(request.IsIdempotent, request.IsSent);

                    // With the retry-policy OtherReplica we add the current endpoint to the list of excluded
                    // endpoints this prevents the endpoints to be tried again during the current retry sequence.
                    if (retryPolicy == RetryPolicy.OtherReplica)
                    {
                        excludedEndpoints ??= new();

                        // This works because we perform the coloc conversion before filtering excluded endpoints.
                        excludedEndpoints.Add(request.Connection.RemoteEndpoint);
                    }

                    // Check if we can retry, we cannot retry if we have consumed all attempts, the current retry
                    // policy doesn't allow retries, the request was already released, or an incoming connection
                    // receives an exception with OtherReplica retry policy.

                    if (attempt == InvocationMaxAttempts ||
                        retryPolicy == RetryPolicy.NoRetry ||
                        (request.IsSent && releaseRequestAfterSent) ||
                        (request.Connection.IsIncoming && retryPolicy == RetryPolicy.OtherReplica))
                    {
                        tryAgain = false;
                    }
                    else
                    {
                        tryAgain = true;
                        attempt++;

                        using IDisposable? socketScope = request.Connection.StartScope();
                        logger.LogRetryRequestRetryableException(
                            retryPolicy,
                            attempt,
                            InvocationMaxAttempts,
                            request,
                            exception);

                        if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                        {
                            // The delay task can be canceled either by the user code using the provided cancellation
                            // token or if the communicator is destroyed.
                            await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                        }

                        if (endpoint != null &&
                            (retryPolicy == RetryPolicy.OtherReplica || !request.Connection.IsActive) &&
                            !request.Connection.IsIncoming)
                        {
                            // Retry with a new connection
                            request.Connection = null;
                        }
                    }
                }
                while (tryAgain);

                if (exception != null)
                {
                    using IDisposable? socketScope = request.Connection?.StartScope();
                    logger.LogRequestException(request, exception);
                }

                Debug.Assert(response != null || exception != null);
                Debug.Assert(response == null || response.ResultType == ResultType.Failure);
                return response ?? throw ExceptionUtil.Throw(exception!);
            }
            finally
            {
                if (!releaseRequestAfterSent)
                {
                    DecRetryBufferSize(requestSize);
                }
                // TODO release the request memory if not already done after sent.
            }

            static bool IsUsable(Endpoint endpoint, bool oneway) =>
                endpoint is not OpaqueEndpoint &&
                endpoint is not UniversalEndpoint &&
                (oneway || !endpoint.IsDatagram);
        }

        /// <summary>Releases all resources used by this communicator. This method can be called multiple times.
        /// </summary>
        /// <returns>A task that completes when the destruction is complete.</returns>
        // TODO: add cancellation token, switch to lazy task pattern
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
                var disposedException = new CommunicatorDisposedException();
                IEnumerable<Task> closeTasks =
                    _outgoingConnections.Values.SelectMany(connections => connections).Select(
                        connection => connection.GoAwayAsync(disposedException));

                await Task.WhenAll(closeTasks).ConfigureAwait(false);

                foreach (Task<Connection> connect in _pendingOutgoingConnections.Values)
                {
                    try
                    {
                        Connection connection = await connect.ConfigureAwait(false);
                        await connection.GoAwayAsync(disposedException).ConfigureAwait(false);
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

        public void Use(params Func<IInvoker, IInvoker>[] interceptor)
        {
            if (_invoker != null)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _invokerInterceptorList = _invokerInterceptorList.AddRange(interceptor);
        }

        public void Use(params Func<ILocationResolver, ILocationResolver>[] interceptor)
        {
            if (_locationResolver != null)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _locationResolverInterceptorList = _locationResolverInterceptorList.AddRange(interceptor);
        }

        internal ValueTask<Connection> BindAsync(
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints,
            CancellationToken cancel)
        {
            if (PreferExistingConnection)
            {
                lock (_mutex)
                {
                    Connection? connection = GetCachedConnection(endpoint);

                    if (connection == null)
                    {
                        foreach (Endpoint altEndpoint in altEndpoints)
                        {
                            connection = GetCachedConnection(altEndpoint);
                            if (connection != null)
                            {
                                break;
                            }
                        }
                    }
                    if (connection != null)
                    {
                        return new(connection);
                    }
                }
            }

            return CreateConnectionAsync();

            async ValueTask<Connection> CreateConnectionAsync()
            {
                OutgoingConnectionOptions connectionOptions = ConnectionOptions ?? OutgoingConnectionOptions.Default;

                try
                {
                    return await ConnectAsync(endpoint, connectionOptions, cancel).ConfigureAwait(false);
                }
                catch
                {
                    foreach (Endpoint altEndpoint in altEndpoints)
                    {
                        try
                        {
                            return await ConnectAsync(altEndpoint, connectionOptions, cancel).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    // If we cannot connect to any endpoint, we throw the first exception
                    // TODO: should we throw an AggregateException instead?
                    throw;
                }
            }

            Connection? GetCachedConnection(Endpoint endpoint) =>
                _outgoingConnections.TryGetValue(endpoint, out LinkedList<Connection>? connections) &&
                connections.FirstOrDefault(connection => connection.IsActive) is Connection connection ?
                    connection : null;
        }

        private IInvoker CreateInvokerPipeline()
        {
            IInvoker pipeline = new InlineInvoker((request, cancel) =>
                request.Connection is Connection connection ? TempConnectionInvokeAsync(connection, request, cancel) :
                    throw new ArgumentNullException($"{nameof(request.Connection)} is null", nameof(request)));

            IEnumerable<Func<IInvoker, IInvoker>> interceptorEnumerable = _invokerInterceptorList;
            foreach (Func<IInvoker, IInvoker> interceptor in interceptorEnumerable.Reverse())
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;

            static async Task<IncomingResponse> TempConnectionInvokeAsync(
                Connection connection,
                OutgoingRequest request,
                CancellationToken cancel)
            {
                SocketStream? stream = null;
                try
                {
                    using IDisposable? socketScope = connection.StartScope();

                    // Create the outgoing stream.
                    stream = connection.CreateStream(!request.IsOneway);

                    // Send the request and wait for the sending to complete.
                    await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                    // TODO: is this correct? When SendRequestFrameAsync throws an exception, we guarantee the request
                    // was not sent?
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
                    // TODO: making SocketStream disposable would be simpler!
                    stream?.Release();
                }
            }
        }

        private ILocationResolver CreateLocationResolverPipeline()
        {
            ILocationResolver pipeline = new InlineLocationResolver(
                (LocEndpoint, refreshCache, cancel) => new((null, ImmutableList<Endpoint>.Empty)));

            IEnumerable<Func<ILocationResolver, ILocationResolver>> interceptorEnumerable =
                _locationResolverInterceptorList;
            foreach (Func<ILocationResolver, ILocationResolver> interceptor in interceptorEnumerable.Reverse())
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;
        }

        private void DecRetryBufferSize(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size <= _retryBufferSize);
                _retryBufferSize -= size;
            }
        }

        private bool IncRetryBufferSize(int size)
        {
            lock (_mutex)
            {
                if (size + _retryBufferSize < RetryBufferMaxSize)
                {
                    _retryBufferSize += size;
                    return true;
                }
            }
            return false;
        }
    }
}
