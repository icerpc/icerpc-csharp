// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
    /// the corresponding responses. A server should be first configured through its properties, then activated with
    /// <see cref="Listen"/> and finally shut down with <see cref="ShutdownAsync"/>.</summary>
    public sealed class Server : IDispatcher, IAsyncDisposable
    {
        // temporary
        public Communicator? Communicator { get; set; }

        /// <summary>Gets or sets the options of incoming connections created by this server.</summary>
        public IncomingConnectionOptions ConnectionOptions { get; set; } = new();

        /// <summary>Gets or sets the dispatcher of this server.</summary>
        /// <value>The dispatcher of this server.</value>
        /// <seealso cref="IDispatcher"/>
        /// <seealso cref="Router"/>
        public IDispatcher? Dispatcher { get; set; }

        /// <summary>Gets or sets the endpoint of this server. Setting this property also sets <see cref="Protocol"/>.
        /// </summary>
        /// <value>The endpoint of this server, for example <c>ice+tcp://[::0]</c>.The endpoint's host is usually an
        /// IP address, and it cannot be a DNS name.</value>
        public string Endpoint
        {
            get => _endpoint?.ToString() ?? "";
            set
            {
                _endpoint = value.Length > 0 ? IceRpc.Endpoint.Parse(value) : null;
                Protocol = _endpoint?.Protocol ?? Protocol.Ice2;
                UpdateProxyEndpoint();
            }
        }

        /// <summary>Gets or sets whether this server can be discovered for colocated calls. Changing this value after
        /// calling <see cref="Listen"/> has no effect.</summary>
        /// <value>True when the server can be discovered for colocated calls; otherwise, false. The default value is
        /// true.</value>
        public bool IsDiscoverable { get; set; }

        /// <summary>Gets or sets the logger factory of this server. When null, the server creates its logger using
        /// <see cref="Runtime.DefaultLoggerFactory"/>.</summary>
        /// <value>The logger factory of this server.</value>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set
            {
                _loggerFactory = value;
                _logger = null; // clears existing logger, if there is one
            }
        }

        /// <summary>Gets of sets the Ice protocol used by this server.</summary>
        /// <value>The Ice protocol of this server.</value>
        public Protocol Protocol { get; set; } = Protocol.Ice2;

        /// <summary>Returns the endpoint included in proxies created by <see cref="CreateProxy"/>. This endpoint is
        /// computed from the values of <see cref="Endpoint"/> and <see cref="ProxyHost"/>.</summary>
        /// <value>An endpoint string when <see cref="Endpoint"/> is not empty; otherwise, an empty string.</value>
        public string ProxyEndpoint => _proxyEndpoint?.ToString() ?? "";

        /// <summary>Gets or sets the host of <see cref="ProxyEndpoint"/> when <see cref="Endpoint"/> uses an IP
        /// address.</summary>
        /// <value>The host or IP address of <see cref="ProxyEndpoint"/>.</value>
        public string ProxyHost
        {
            get => _proxyHost;
            set
            {
                if (value.Length == 0)
                {
                    throw new ArgumentException($"{nameof(ProxyHost)} must have at least one character",
                                                nameof(ProxyHost));
                }
                _proxyHost = value;
                UpdateProxyEndpoint();
            }
        }

        /// <summary>The options of proxies received in requests or created using this server.</summary>
        public ProxyOptions ProxyOptions { get; set; } = new();

        /// <summary>Returns a task that completes when the server's shutdown is complete: see
        /// <see cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        internal ILogger Logger => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");

        private static ulong _counter; // used to generate names for servers without endpoints

        private readonly CancellationTokenSource _cancelDispatchSource = new();

        private AcceptorIncomingConnectionFactory? _colocConnectionFactory;
        private ColocEndpoint? _colocEndpoint;

        private readonly string _colocName = $"colocated-{Interlocked.Increment(ref _counter)}";

        private Endpoint? _endpoint;

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private IncomingConnectionFactory? _incomingConnectionFactory;
        private bool _listening;

        // protects _shutdownTask
        private readonly object _mutex = new();

        private Endpoint? _proxyEndpoint;

        private string _proxyHost = "localhost"; // temporary default

        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Lazy<Task>? _shutdownTask;

        /// <summary>Creates a relative proxy for a service hosted by this server. This relative proxy holds a colocated
        /// connection to this server.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="path">The path of the service.</param>
        /// <returns>A new relative proxy.</returns>
        public T CreateRelativeProxy<T>(string path) where T : class, IServicePrx
        {
            // temporary
            ProxyOptions.Communicator ??= Communicator;

            return Proxy.GetFactory<T>().Create(path,
                                                Protocol,
                                                Protocol.GetEncoding(),
                                                ImmutableList<Endpoint>.Empty,
                                                GetColocConnection(),
                                                ProxyOptions);
        }

        /// <summary>Creates a proxy for a service hosted by this server.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="path">The path of the service.</param>
        /// <returns>A new proxy with a single endpoint, <see cref="ProxyEndpoint"/>.</returns>
        public T CreateProxy<T>(string path) where T : class, IServicePrx
        {
            if (_proxyEndpoint == null)
            {
                throw new InvalidOperationException("cannot create a proxy using a server with no endpoint");
            }

            ProxyOptions options = ProxyOptions;
            options.Communicator ??= Communicator;

            if (_proxyEndpoint.IsDatagram && !options.IsOneway)
            {
                options = options.Clone();
                options.IsOneway = true;
            }

            return Proxy.GetFactory<T>().Create(path,
                                                Protocol,
                                                Protocol.GetEncoding(),
                                                ImmutableList.Create(_proxyEndpoint),
                                                connection: null,
                                                options);
        }

        /// <summary>Dispatches a request by calling <see cref="IDispatcher.DispatchAsync"/> on the configured
        /// <see cref="Dispatcher"/>. If <c>DispatchAsync</c> throws a <see cref="RemoteException"/> with
        /// <see cref="RemoteException.ConvertToUnhandled"/> set to true, this method converts this exception into an
        /// <see cref="UnhandledException"/> response. If <see cref="Dispatcher"/> is null, this method returns a
        /// <see cref="ServiceNotFoundException"/> response.</summary>
        /// <param name="current">The request being dispatched.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that provides the <see cref="OutgoingResponseFrame"/> for the request.</returns>
        /// <remarks>This method is called by the IceRPC connection code when it receives a request.</remarks>
        async ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel)
        {
            // temporary
            ProxyOptions.Communicator ??= Communicator;

            if (!_listening)
            {
                var ex = new UnhandledException(
                    new InvalidOperationException($"call {nameof(Listen)} before dispatching colocated requests"));

                return new OutgoingResponseFrame(current.IncomingRequestFrame, ex);
            }

            if (Dispatcher is IDispatcher dispatcher)
            {
                // cancel is canceled when the client cancels the call (resets the stream). We construct a separate
                // source/token that combines cancel and the server's own cancellation token.
                using var combinedSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancel, _cancelDispatchSource.Token);

                try
                {
                    OutgoingResponseFrame response =
                        await dispatcher.DispatchAsync(current, combinedSource.Token).ConfigureAwait(false);

                    cancel.ThrowIfCancellationRequested();
                    return response;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    // The client requested cancellation, we log it and let it propagate.
                    Logger.LogServerDispatchCanceledByClient(current);
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException && _cancelDispatchSource.Token.IsCancellationRequested)
                    {
                        // Replace exception
                        ex = new ServerException("dispatch canceled by server shutdown");
                    }
                    // else it's another OperationCanceledException that the implementation should have caught, and it
                    // will become an UnhandledException below.

                    if (current.IsOneway)
                    {
                        // We log this exception, since otherwise it would be lost.
                        Logger.LogServerDispatchException(current, ex);
                        return OutgoingResponseFrame.WithVoidReturnValue(current);
                    }
                    else
                    {
                        RemoteException actualEx;
                        if (ex is RemoteException remoteEx && !remoteEx.ConvertToUnhandled)
                        {
                            actualEx = remoteEx;
                        }
                        else
                        {
                            actualEx = new UnhandledException(ex);

                            // We log the "source" exception as UnhandledException may not include all details.
                            Logger.LogServerDispatchException(current, ex);
                        }
                        return new OutgoingResponseFrame(current.IncomingRequestFrame, actualEx);
                    }
                }
            }
            else
            {
                return new OutgoingResponseFrame(current.IncomingRequestFrame,
                                                 new ServiceNotFoundException(RetryPolicy.OtherReplica));
            }
        }

        /// <summary>Starts listening on the configured endpoint (if any) and serving clients (by dispatching their
        /// requests). If the configured endpoint is an IP endpoint with port 0, this method updates the endpoint to
        /// include the actual port selected by the operating system.</summary>
        /// <exception cref="InvalidOperationException">Thrown when the server is already listening.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the server is shut down or shutting down.</exception>
        /// <exception cref="TransportException">Thrown when another server is already listening on the same endpoint.
        /// </exception>
        public void Listen()
        {
            // We lock the mutex because ShutdownAsync can run concurrently.
            lock (_mutex)
            {
                if (_listening)
                {
                    throw new InvalidOperationException($"server '{this}' is already listening");
                }

                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                if (_endpoint == null)
                {
                    // Temporary
                    Endpoint = Protocol == Protocol.Ice2 ? $"ice+coloc://{_colocName}" : $"coloc -h {_colocName}";
                }
                Debug.Assert(_endpoint != null);

                _incomingConnectionFactory = _endpoint.IsDatagram ?
                    new DatagramIncomingConnectionFactory(this, _endpoint) :
                    new AcceptorIncomingConnectionFactory(this, _endpoint);

                _endpoint = _incomingConnectionFactory.Endpoint;
                UpdateProxyEndpoint();

                _incomingConnectionFactory.Activate();

                // In theory, as soon as we register this server for coloc, a coloc call could/should succeed.
                _listening = true;

                if (IsDiscoverable && _endpoint.Transport != Transport.Coloc && !_endpoint.IsDatagram)
                {
                    if (_colocEndpoint == null) // temporary check, we don't really need _colocEndpoint
                    {
                        _colocEndpoint = new ColocEndpoint(host: $"{_endpoint.Host}.{_endpoint.TransportName}",
                                                           port: _endpoint.Port,
                                                           protocol: _endpoint.Protocol);

                        _colocConnectionFactory = new AcceptorIncomingConnectionFactory(this, _colocEndpoint);
                        _colocConnectionFactory.Activate();
                    }

                    EndpointExtensions.RegisterColocEndpoint(_endpoint, _colocEndpoint);
                }

                // TODO: remove
                if (Communicator?.GetPropertyAsBool("Ice.PrintAdapterReady") ?? false)
                {
                    Console.Out.WriteLine($"{this} ready");
                }

                Logger.LogServerListening(this);
            }
        }

        /// <summary>Shuts down this server: the server stops accepting new connections and requests, waits for all
        /// outstanding dispatches to complete and gracefully closes all its incoming connections. Once shut down, a
        /// server is disposed and can no longer be used. This method can be safely called multiple times, including
        /// from multiple threads.</summary>
        /// <param name="cancel">The cancellation token. When this token is canceled, the cancellation token of all
        /// outstanding dispatches is canceled, which can speed up the shutdown provided the operation implementations
        /// check their cancellation tokens.</param>
        /// <return>A task that completes once the shutdown is complete.</return>
        public async Task ShutdownAsync(CancellationToken cancel = default)
        {
            // We create the lazy shutdown task with the mutex locked then we create the actual task immediately (and
            // synchronously) after releasing the lock.
            lock (_mutex)
            {
                _shutdownTask ??= new Lazy<Task>(() => PerformShutdownAsync());
            }

            try
            {
                await _shutdownTask.Value.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    // When the caller requests cancellation, we signal _cancelDispatchSource.
                    _cancelDispatchSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // ignored, can occur with multiple / concurrent calls to ShutdownAsync/DisposeAsync
                }
                await _shutdownTask.Value.ConfigureAwait(false);
            }

            async Task PerformShutdownAsync()
            {
                try
                {
                    Logger.LogServerShuttingDown(this);

                    // No longer available for coloc connections (may not be registered at all)
                    if (_endpoint is Endpoint endpoint && endpoint.Transport != Transport.Coloc)
                    {
                        EndpointExtensions.UnregisterColocEndpoint(endpoint);
                    }

                    // Shuts down the incoming connection factory to stop accepting new incoming requests or
                    // connections. This ensures that once ShutdownAsync returns, no new requests will be dispatched.
                    // Once _shutdownTask is non null, _incomingConnectionfactory cannot change, so no need to lock
                    // _mutex.
                    Task colocShutdownTask = _colocConnectionFactory?.ShutdownAsync() ?? Task.CompletedTask;
                    Task incomingShutdownTask = _incomingConnectionFactory?.ShutdownAsync() ?? Task.CompletedTask;

                    await Task.WhenAll(colocShutdownTask, incomingShutdownTask).ConfigureAwait(false);
                }
                finally
                {
                    Logger.LogServerShutdownComplete(this);

                    // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
                    // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
                    // using Result or Wait()), ShutdownAsync will complete.
                    _shutdownCompleteSource.TrySetResult(null);
                }
            }
        }

        /// <inherit-doc/>
        public override string ToString() => _endpoint?.ToString() ?? _colocName;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await ShutdownAsync(new CancellationToken(canceled: true)).ConfigureAwait(false);
            _cancelDispatchSource.Dispose();
        }

        // Proxies which have at least one endpoint in common with the endpoints used by this server are considered
        // colocated. Called by ColocServerRegistry.
        internal Endpoint? GetColocEndpoint(ServicePrx proxy) =>
            proxy.Endpoints.Any(endpoint => endpoint.IsEquivalent(_endpoint!) ||
                                            endpoint.IsEquivalent(_proxyEndpoint!)) ?
                GetColocEndpoint() : null;

        private Connection? GetColocConnection()
        {
            if (GetColocEndpoint() is Endpoint endpoint)
            {
                // TODO: very temporary code
                ValueTask<Connection> vt =
                    Communicator!.ConnectAsync(endpoint, Communicator.ConnectionOptions, default);
                return vt.IsCompleted ? vt.Result : vt.AsTask().Result;
            }
            else
            {
                return null;
            }
        }

        private Endpoint? GetColocEndpoint()
        {
            // Lazy initialized because it needs a fully configured server, in particular Protocol.
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    return null;
                }

                if (_colocEndpoint == null)
                {
                    string host;
                    ushort port;

                    if (_endpoint is Endpoint endpoint)
                    {
                        host = $"{endpoint.Host}.{endpoint.TransportName}";
                        port = endpoint.Port;
                    }
                    else
                    {
                        host = $"{_colocName}.coloc";
                        port = 4062;
                    }
                    _colocEndpoint = new ColocEndpoint(host, port, Protocol);
                    _colocConnectionFactory = new AcceptorIncomingConnectionFactory(this, _colocEndpoint);
                    _colocConnectionFactory.Activate();
                }
            }
            return _colocEndpoint;
        }

        private void UpdateProxyEndpoint() => _proxyEndpoint = _endpoint?.GetProxyEndpoint(ProxyHost);
    }
}
