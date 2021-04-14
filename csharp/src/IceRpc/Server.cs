// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
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
    // temporary
    public enum ColocationScope
    {
        Process,
        Communicator,
        None
    }

    /// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
    /// the corresponding responses. A server should be first configured through its properties, then activated with
    /// <see cref="ListenAndServeAsync"/> and finally shut down with <see cref="ShutdownAsync"/>.</summary>
    public sealed class Server : IDispatcher, IAsyncDisposable
    {
        // temporary
        public ColocationScope ColocationScope { get; set; } = ColocationScope.Communicator;

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

        private AcceptorIncomingConnectionFactory? _colocatedConnectionFactory;
        private readonly string _colocatedName = $"colocated-{Interlocked.Increment(ref _counter)}";

        private Endpoint? _endpoint;

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private IncomingConnectionFactory? _incomingConnectionFactory;

        // protects _shutdownTask
        private readonly object _mutex = new();

        private Endpoint? _proxyEndpoint;

        private string _proxyHost = "localhost"; // temporary default

        private bool _serving;

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
                                                GetColocatedConnection(),
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
                                                connection: null, // TODO: give it a coloc connection except for UDP?
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
        /// <remarks>This method is called by the IceRPC transport code when it receives a request. It does not throw
        /// any exception, synchronously or asynchronously.</remarks>
        async ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel)
        {
            // temporary
            ProxyOptions.Communicator ??= Communicator;

            if (!_serving)
            {
                var ex = new UnhandledException(
                    new InvalidOperationException(
                        $"call {nameof(ListenAndServeAsync)} before dispatching colocated requests"));

                return new OutgoingResponseFrame(current.IncomingRequestFrame, ex);
            }

            if (Dispatcher is IDispatcher dispatcher)
            {
                try
                {
                    return await dispatcher.DispatchAsync(current, cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (current.IsOneway)
                    {
                        // We log this exception, since otherwise it would be lost.
                        // TODO: use a server event for this logging?
                        Logger.LogDispatchException(current.IncomingRequestFrame, ex);

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
                            // TODO: use a server event for this logging?
                            Logger.LogDispatchException(current.IncomingRequestFrame, ex);
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
        /// include the actual port selected by the operating system. This method throws start-up exceptions
        /// synchronously; for  example, if another server is already listening on the configured endpoint, it throws a
        /// <see cref="TransportException"/> synchronously.</summary>
        /// <param name="cancel">The cancellation token. If the caller cancels this token, the server calls
        /// <see cref="ShutdownAsync"/> with this cancellation token.</param>
        /// <return>A task that completes once <see cref="ShutdownComplete"/> is complete.</return>
        public Task ListenAndServeAsync(CancellationToken cancel = default)
        {
            if (_serving)
            {
                throw new InvalidOperationException(
                    $"'{nameof(ListenAndServeAsync)}' was already called on server '{this}'");
            }

            // We lock the mutex because it's ok for ShutdownAsync to be called concurrently.
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                if (_endpoint is Endpoint endpoint)
                {
                    _incomingConnectionFactory = endpoint.IsDatagram ?
                        new DatagramIncomingConnectionFactory(this, endpoint) :
                        new AcceptorIncomingConnectionFactory(this, endpoint);

                    _endpoint = _incomingConnectionFactory.Endpoint;
                    UpdateProxyEndpoint();

                    _incomingConnectionFactory.Activate();
                    _serving = true;
                }

                if (ColocationScope != ColocationScope.None)
                {
                    LocalServerRegistry.RegisterServer(this);
                }
            }

            _serving = true;

            if (Communicator?.GetPropertyAsBool("Ice.PrintAdapterReady") ?? false)
            {
                Console.Out.WriteLine($"{this} ready");
            }

            Logger.LogServerListeningAndServing(this);

            return WaitForShutdownAsync(cancel);

            async Task WaitForShutdownAsync(CancellationToken cancel)
            {
                try
                {
                    await ShutdownComplete.WaitAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Request "quick" shutdown that completes as soon as possible by canceling everything it can.
                    _ = ShutdownAsync(cancel);
                    await ShutdownComplete.ConfigureAwait(false);
                }
            }
        }

        /// <summary>Shuts down this server. Once shut down, a server is disposed and can no longer be used. This method
        /// can be safely called multiple times, including from multiple threads, and always returns the same task.
        /// </summary>
        /// <param name="_">The cancellation token. If the caller cancels this token, this method completes as
        /// quickly as possible by cancelling outstanding requests and closing connections without waiting.</param>
        /// <return>A task that completes once the shutdown is complete.</return>
        // TODO: implement cancellation
        public Task ShutdownAsync(CancellationToken _ = default)
        {
            // We create the lazy shutdown task with the mutex locked then we create the actual task immediately (and
            // synchronously) after releasing the lock.
            lock (_mutex)
            {
                _shutdownTask ??= new Lazy<Task>(() => PerformShutdownAsync());
            }
            return _shutdownTask.Value;

            async Task PerformShutdownAsync()
            {
                try
                {
                    Logger.LogServerShuttingDown(this);

                    if (ColocationScope != ColocationScope.None)
                    {
                        // no longer available for coloc connections.
                        LocalServerRegistry.UnregisterServer(this);
                    }

                    // Shuts down the incoming connection factory to stop accepting new incoming requests or
                    // connections. This ensures that once ShutdownAsync returns, no new requests will be dispatched.
                    // Once _shutdownTask is non null, _incomingConnectionfactory cannot change, so no need to lock
                    // _mutex.
                    Task? colocShutdownTask = _colocatedConnectionFactory?.ShutdownAsync();
                    Task? incomingShutdownTask = _incomingConnectionFactory?.ShutdownAsync();

                    if (colocShutdownTask != null && incomingShutdownTask != null)
                    {
                        await Task.WhenAll(colocShutdownTask, incomingShutdownTask).ConfigureAwait(false);
                    }
                    else if (colocShutdownTask != null || incomingShutdownTask != null)
                    {
                        await (colocShutdownTask ?? incomingShutdownTask)!.ConfigureAwait(false);
                    }
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
        public override string ToString() => _endpoint?.ToString() ?? _colocatedName;

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        internal Endpoint GetColocatedEndpoint()
        {
            // Lazy initialized because it needs a fully configured server, in particular Protocol.
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                if (_colocatedConnectionFactory == null)
                {
                    _colocatedConnectionFactory
                        = new AcceptorIncomingConnectionFactory(this, new ColocatedEndpoint(this));
                    _colocatedConnectionFactory.Activate();
                }
            }
            return _colocatedConnectionFactory.Endpoint;
        }

        internal Endpoint? GetColocatedEndpoint(ServicePrx proxy)
        {
            Debug.Assert(ColocationScope != ColocationScope.None);

            if (ColocationScope == ColocationScope.Communicator && Communicator != proxy.Communicator)
            {
                return null;
            }

            if (proxy.Protocol != Protocol)
            {
                return null;
            }

            bool isLocal = false;

            lock (_mutex)
            {
                // Proxies which have at least one endpoint in common with the endpoints used by this object
                // server's incoming connection factories are considered local.
                isLocal = _shutdownTask == null &&
                    proxy.Endpoints.Any(endpoint => endpoint == _endpoint || endpoint == _proxyEndpoint);
            }

            return isLocal ? GetColocatedEndpoint() : null;
        }

        private Connection GetColocatedConnection()
        {
            // TODO: very temporary code
            var vt = Communicator!.ConnectAsync(GetColocatedEndpoint(), new(), default);
            return vt.IsCompleted ? vt.Result : vt.AsTask().Result;
        }

        private void UpdateProxyEndpoint() => _proxyEndpoint = _endpoint?.GetPublishedEndpoint(ProxyHost);
    }
}
