// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
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
        /// <summary>The connection options.</summary>
        public OutgoingConnectionOptions ConnectionOptions;

        /// <summary>The output mode or format for ToString on Ice proxies when the protocol is ice1. See
        /// <see cref="IceRpc.Interop.ToStringMode"/>.</summary>
        public ToStringMode ToStringMode { get; }

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

        /// <summary>Gets the maximum number of invocation attempts made to send a request including the original
        /// invocation. It must be a number greater than 0.</summary>
        internal int InvocationMaxAttempts { get; }

        /// <summary>The default logger for this communicator.</summary>
        internal ILogger Logger { get; }
        internal int RetryBufferMaxSize { get; }
        internal int RetryRequestMaxSize { get; }

        private static string[] _emptyArgs = Array.Empty<string>();

        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private ImmutableList<Func<IInvoker, IInvoker>> _interceptorList =
            ImmutableList<Func<IInvoker, IInvoker>>.Empty;
        private IInvoker? _invoker;

        private Task? _shutdownTask;

        private readonly object _mutex = new();

        private int _retryBufferSize;

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            IReadOnlyDictionary<string, string> properties,
            ILoggerFactory? loggerFactory = null,
            OutgoingConnectionOptions? connectionOptions = null)
            : this(ref _emptyArgs,
                   appSettings: null,
                   loggerFactory,
                   properties,
                   connectionOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            ref string[] args,
            IReadOnlyDictionary<string, string> properties,
            ILoggerFactory? loggerFactory = null,
            OutgoingConnectionOptions? connectionOptions = null)
            : this(ref args,
                   appSettings: null,
                   loggerFactory,
                   properties,
                   connectionOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            NameValueCollection? appSettings = null,
            ILoggerFactory? loggerFactory = null,
            IReadOnlyDictionary<string, string>? properties = null,
            OutgoingConnectionOptions? connectionOptions = null)
            : this(ref _emptyArgs,
                   appSettings,
                   loggerFactory,
                   properties,
                   connectionOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="loggerFactory">The loggerFactory used by the new communicator.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            ref string[] args,
            NameValueCollection? appSettings = null,
            ILoggerFactory? loggerFactory = null,
            IReadOnlyDictionary<string, string>? properties = null,
            OutgoingConnectionOptions? connectionOptions = null)
        {
            loggerFactory ??= NullLoggerFactory.Instance;
            Logger = loggerFactory.CreateLogger("IceRpc");

            // clone properties as we don't want to modify the properties given to this constructor
            var combinedProperties =
                new Dictionary<string, string>(properties ?? ImmutableDictionary<string, string>.Empty);

            if (appSettings != null)
            {
                foreach (string? key in appSettings.AllKeys)
                {
                    if (key != null)
                    {
                        string[]? values = appSettings.GetValues(key);
                        if (values == null)
                        {
                            combinedProperties[key] = "";
                        }
                        else if (values.Length == 1)
                        {
                            combinedProperties[key] = values[0];
                        }
                        else
                        {
                            combinedProperties[key] = StringUtil.ToPropertyValue(values);
                        }
                    }
                }
            }

            combinedProperties.ParseIceArgs(ref args);
            SetProperties(combinedProperties);
            Logger = loggerFactory.CreateLogger("IceRpc");

            ConnectionOptions = connectionOptions?.Clone() ?? new OutgoingConnectionOptions();
            ConnectionOptions.TransportOptions ??= new TcpOptions();

            ConnectionOptions.IncomingFrameMaxSize =
                this.GetPropertyAsByteSize("Ice.IncomingFrameMaxSize") ?? ConnectionOptions.IncomingFrameMaxSize;

            InvocationMaxAttempts = this.GetPropertyAsInt("Ice.InvocationMaxAttempts") ?? 5;

            if (InvocationMaxAttempts <= 0)
            {
                throw new InvalidConfigurationException($"Ice.InvocationMaxAttempts must be greater than 0");
            }
            InvocationMaxAttempts = Math.Min(InvocationMaxAttempts, 5);
            RetryBufferMaxSize = this.GetPropertyAsByteSize("Ice.RetryBufferMaxSize") ?? 1024 * 1024 * 100;
            RetryRequestMaxSize = this.GetPropertyAsByteSize("Ice.RetryRequestMaxSize") ?? 1024 * 1024;

            ToStringMode = this.GetPropertyAsEnum<ToStringMode>("Ice.ToStringMode") ?? default;
        }

        Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            (_invoker ??= CreatePipeline()).InvokeAsync(request, cancel);

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
                        connection => connection.ShutdownAsync(disposedException));

                await Task.WhenAll(closeTasks).ConfigureAwait(false);

                foreach (Task<Connection> connect in _pendingOutgoingConnections.Values)
                {
                    try
                    {
                        Connection connection = await connect.ConfigureAwait(false);
                        await connection.ShutdownAsync(disposedException).ConfigureAwait(false);
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

        /// <summary>An alias for <see cref="ShutdownAsync"/>, except this method returns a <see cref="ValueTask"/>.
        /// </summary>
        /// <returns>A value task constructed using the task returned by ShutdownAsync.</returns>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        public void Use(params Func<IInvoker, IInvoker>[] interceptor)
        {
            if (_invoker != null)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _interceptorList = _interceptorList.AddRange(interceptor);
        }

        // TODO: with the current logic, all interceptors actually execute before bind, whether bind is needed or not.
        public void UseBeforeBind(params Func<IInvoker, IInvoker>[] interceptor) =>
            throw new NotImplementedException();

        internal async ValueTask<List<Endpoint>> ComputeEndpointsAsync(
            ServicePrx proxy,
            bool refreshCache,
            bool oneway,
            CancellationToken cancel)
        {
            if (proxy.ParsedEndpoint?.ToColocEndpoint() is Endpoint colocEndpoint)
            {
                return new List<Endpoint>() { colocEndpoint };
            }

            foreach (Endpoint endpoint in proxy.ParsedAltEndpoints)
            {
                if (endpoint.ToColocEndpoint() is Endpoint colocAltEndpoint)
                {
                    return new List<Endpoint>() { colocAltEndpoint };
                }
            }

            IEnumerable<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;

            // Get the proxy's endpoint or query the location resolver to get endpoints.

            if (proxy.IsIndirect)
            {
                if (proxy.LocationResolver is ILocationResolver locationResolver)
                {
                    endpoints = await locationResolver.ResolveAsync(proxy.ParsedEndpoint!,
                                                                    refreshCache,
                                                                    cancel).ConfigureAwait(false);
                }
                // else endpoints remains empty.
            }
            else if (proxy.ParsedEndpoint != null)
            {
                endpoints = ImmutableList.Create(proxy.ParsedEndpoint).AddRange(proxy.ParsedAltEndpoints);
            }

            // Apply overrides and filter endpoints
            var filteredEndpoints = endpoints.Where(endpoint =>
            {
                // Filter out opaque and universal endpoints
                if (endpoint is OpaqueEndpoint || endpoint is UniversalEndpoint)
                {
                    return false;
                }

                // Filter out datagram endpoints when oneway is false.
                if (endpoint.IsDatagram)
                {
                    return oneway;
                }

                return true;
            }).ToList();

            if (filteredEndpoints.Count == 0)
            {
                throw new NoEndpointException(proxy.ToString());
            }

            if (filteredEndpoints.Count > 1)
            {
                filteredEndpoints = OrderEndpointsByTransportFailures(filteredEndpoints);
            }
            return filteredEndpoints;
        }

        internal void DecRetryBufferSize(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size <= _retryBufferSize);
                _retryBufferSize -= size;
            }
        }

        internal bool IncRetryBufferSize(int size)
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

        private IInvoker CreatePipeline()
        {
            IInvoker pipeline = new InlineInvoker(async (request, cancel) =>
            {
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
                    return await PerformInvokeAsync(request, releaseRequestAfterSent, cancel).ConfigureAwait(false);
                }
                finally
                {
                    if (!releaseRequestAfterSent)
                    {
                        DecRetryBufferSize(requestSize);
                    }
                    // TODO release the request memory if not already done after sent.
                }
            });

            IEnumerable<Func<IInvoker, IInvoker>> interceptorEnumerable = _interceptorList;
            foreach (Func<IInvoker, IInvoker> interceptor in interceptorEnumerable.Reverse())
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;
        }

        private async Task<IncomingResponse> PerformInvokeAsync(
            OutgoingRequest request,
            bool releaseRequestAfterSent,
            CancellationToken cancel)
        {
            ServicePrx proxy = request.Proxy.Impl;

            Connection? connection = proxy.Connection;
            List<Endpoint>? endpoints = null;
            bool oneway = request.IsOneway;
            IProgress<bool>? progress = request.Progress;

            if (connection != null && !oneway && connection.IsDatagram)
            {
                throw new InvalidOperationException(
                    "cannot make two-way invocation using a cached datagram connection");
            }

            if ((connection == null || (proxy.ParsedEndpoint != null && !connection.IsActive)) && proxy.PreferExistingConnection)
            {
                // No cached connection, so now check if there is an existing connection that we can reuse.
                endpoints =
                    await ComputeEndpointsAsync(proxy, refreshCache: false, oneway, cancel).ConfigureAwait(false);
                connection = GetConnection(endpoints);
                if (proxy.CacheConnection)
                {
                    proxy.Connection = connection;
                }
            }

            OutgoingConnectionOptions connectionOptions = ConnectionOptions.Clone();

            ILogger logger = Logger;
            int nextEndpoint = 0;
            int attempt = 1;
            bool triedAllEndpoints = false;
            List<Endpoint>? excludedEndpoints = null;
            IncomingResponse? response = null;
            Exception? exception = null;

            bool tryAgain = false;

            if (Activity.Current != null && Activity.Current.Id != null)
            {
                request.WriteActivityContext(Activity.Current);
            }

            do
            {
                bool sent = false;
                SocketStream? stream = null;
                try
                {
                    if (connection == null)
                    {
                        if (endpoints == null)
                        {
                            Debug.Assert(nextEndpoint == 0);

                            // ComputeEndpointsAsync throws if it can't figure out the endpoints
                            // We also request fresh endpoints when retrying, but not for the first attempt.
                            endpoints = await ComputeEndpointsAsync(proxy,
                                                                    refreshCache: tryAgain,
                                                                    oneway,
                                                                    cancel).ConfigureAwait(false);
                            if (excludedEndpoints != null)
                            {
                                endpoints = endpoints.Except(excludedEndpoints).ToList();
                                if (endpoints.Count == 0)
                                {
                                    endpoints = null;
                                    throw new NoEndpointException();
                                }
                            }
                        }

                        connection = await ConnectAsync(endpoints[nextEndpoint],
                                                        connectionOptions,
                                                        cancel).ConfigureAwait(false);

                        if (proxy.CacheConnection)
                        {
                            proxy.Connection = connection;
                        }
                    }

                    cancel.ThrowIfCancellationRequested();

                    response?.Dispose();
                    response = null;

                    using IDisposable? socketScope = connection.StartScope();

                    // Create the outgoing stream.
                    stream = connection.CreateStream(!oneway);

                    // Send the request and wait for the sending to complete.
                    await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                    using IDisposable? streamSocket = stream.StartScope();
                    logger.LogSentRequest(request);

                    // The request is sent, notify the progress callback.
                    // TODO: Get rid of the sentSynchronously parameter which is always false now?
                    if (progress != null)
                    {
                        progress.Report(false);
                        progress = null; // Only call the progress callback once (TODO: revisit this?)
                    }
                    if (releaseRequestAfterSent)
                    {
                        // TODO release the request
                    }
                    sent = true;
                    exception = null;

                    if (oneway)
                    {
                        return new IncomingResponse(connection, request.PayloadEncoding);
                    }

                    // Wait for the reception of the response.
                    response = await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);
                    response.Connection = connection;

                    logger.LogReceivedResponse(response);

                    // If success, just return the response!
                    if (response.ResultType == ResultType.Success)
                    {
                        return response;
                    }
                }
                catch (NoEndpointException ex) when (tryAgain)
                {
                    // If we get NoEndpointException while retrying, either all endpoints have been excluded or the
                    // proxy has no endpoints. So we cannot retry, and we return here to preserve any previous
                    // exception that might have been thrown.
                    return response ?? throw exception ?? ex;
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    stream?.Release();
                }

                // Compute retry policy based on the exception or response retry policy, whether or not the connection
                // is established or the request sent and idempotent
                Debug.Assert(response != null || exception != null);
                RetryPolicy retryPolicy =
                    response?.GetRetryPolicy(proxy) ?? exception!.GetRetryPolicy(request.IsIdempotent, sent);

                // With the retry-policy OtherReplica we add the current endpoint to the list of excluded
                // endpoints this prevents the endpoints to be tried again during the current retry sequence.
                if (retryPolicy == RetryPolicy.OtherReplica &&
                    (endpoints?[nextEndpoint] ?? connection?.RemoteEndpoint) is Endpoint endpoint)
                {
                    excludedEndpoints ??= new();
                    excludedEndpoints.Add(endpoint);
                }

                if (endpoints != null && (connection == null || retryPolicy == RetryPolicy.OtherReplica))
                {
                    // If connection establishment failed or if the endpoint was excluded, try the next endpoint
                    nextEndpoint = ++nextEndpoint % endpoints.Count;
                    if (nextEndpoint == 0)
                    {
                        // nextEndpoint == 0 indicates that we already tried all the endpoints.
                        if (proxy.IsIndirect && !tryAgain)
                        {
                            // If we were potentially using cached endpoints, so we clear the endpoints before trying
                            // again.
                            endpoints = null;
                        }
                        else
                        {
                            // Otherwise we set triedAllEndpoints to true to ensure further connection establishment
                            // failures will now count as attempts (to prevent indefinitely looping if connection
                            // establishment failure results in a retryable exception).
                            triedAllEndpoints = true;
                            if (excludedEndpoints != null)
                            {
                                endpoints = endpoints.Except(excludedEndpoints).ToList();
                            }
                        }
                    }
                }

                // Check if we can retry, we cannot retry if we have consumed all attempts, the current retry
                // policy doesn't allow retries, the request was already released, there are no more endpoints
                // or an incoming connection receives an exception with OtherReplica retry policy.

                if (attempt == InvocationMaxAttempts ||
                    retryPolicy == RetryPolicy.NoRetry ||
                    (sent && releaseRequestAfterSent) ||
                    (triedAllEndpoints && endpoints != null && endpoints.Count == 0) ||
                    ((connection?.IsIncoming ?? false) && retryPolicy == RetryPolicy.OtherReplica))
                {
                    tryAgain = false;
                }
                else
                {
                    tryAgain = true;

                    // Only count an attempt if the connection was established or if all the endpoints were tried
                    // at least once. This ensures that we don't end up into an infinite loop for connection
                    // establishment failures which don't result in endpoint exclusion.
                    if (connection != null)
                    {
                        attempt++;

                        using IDisposable? socketScope = connection?.StartScope();
                        logger.LogRetryRequestRetryableException(
                            retryPolicy,
                            attempt,
                            InvocationMaxAttempts,
                            request,
                            exception);
                    }
                    else if (triedAllEndpoints)
                    {
                        attempt++;

                        logger.LogRetryRequestConnectionException(
                            retryPolicy,
                            attempt,
                            InvocationMaxAttempts,
                            request,
                            exception);
                    }

                    if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                    {
                        // The delay task can be canceled either by the user code using the provided cancellation
                        // token or if the communicator is destroyed.
                        await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                    }

                    if (proxy.ParsedEndpoint != null && connection != null && !connection.IsIncoming)
                    {
                        // Retry with a new connection!
                        connection = null;
                    }
                }
            }
            while (tryAgain);

            if (exception != null)
            {
                using IDisposable? socketScope = connection?.StartScope();
                logger.LogRequestException(request, exception);
            }

            Debug.Assert(response != null || exception != null);
            Debug.Assert(response == null || response.ResultType == ResultType.Failure);
            return response ?? throw ExceptionUtil.Throw(exception!);
        }
    }
}
