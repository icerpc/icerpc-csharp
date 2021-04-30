// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
    /// the corresponding responses. A server should be first configured through its properties, then activated with
    /// <see cref="Listen"/> and finally shut down with <see cref="ShutdownAsync"/>.</summary>
    public sealed class Server : IAsyncDisposable
    {
        /// <summary>When set to a non null value it is used as the source to create <see cref="Activity"/>
        /// instances for dispatches.</summary>
        public ActivitySource? ActivitySource { get; set; }

        /// <summary>Gets or sets the options of incoming connections created by this server.</summary>
        public IncomingConnectionOptions ConnectionOptions { get; set; } = new();

        /// <summary>Gets or sets the dispatcher of this server.</summary>
        /// <value>The dispatcher of this server.</value>
        /// <seealso cref="IDispatcher"/>
        /// <seealso cref="Router"/>
        public IDispatcher? Dispatcher { get; set; }

        /// <summary>Gets or sets the endpoint of this server.</summary>
        /// <value>The endpoint of this server, for example <c>ice+tcp://[::0]</c>.The endpoint's host is usually an
        /// IP address, and it cannot be a DNS name.</value>
        public string Endpoint
        {
            get => _endpoint?.ToString() ?? "";
            set
            {
                if (_listening)
                {
                    throw new InvalidOperationException("cannot change the endpoint of a server after calling Listen");
                }

                _endpoint = value.Length > 0 ? IceRpc.Endpoint.Parse(value) : null;
                UpdateProxyEndpoint();
            }
        }

        /// <summary>Gets or sets whether this server listens on an endpoint for the coloc transport in addition to its
        /// regular endpoint. This property has no effect when <see cref="Endpoint"/>'s transport is coloc. Changing
        /// this value after calling <see cref="Listen"/> has no effect as well.</summary>
        /// <value>True when the server listens on an endpoint for the coloc transport; otherwise, false. The default
        /// value is true.</value>
        public bool HasColocEndpoint { get; set; } = true;

        /// <summary>The invoker of proxies created or unmarshaled by this server.</summary>
        public IInvoker? Invoker { get; set; }

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

        /// <summary>Gets the Ice protocol used by this server.</summary>
        /// <value>The Ice protocol of this server.</value>
        public Protocol Protocol => _endpoint?.Protocol ?? Protocol.Ice2;

        /// <summary>Returns the endpoint included in proxies created by <see cref="CreateProxy"/>. This endpoint is
        /// computed from the values of <see cref="Endpoint"/> and <see cref="ProxyHost"/>.</summary>
        /// <value>An endpoint string when <see cref="Endpoint"/> is not empty; otherwise, an empty string.</value>
        public string ProxyEndpoint => _proxyEndpoint?.ToString() ?? "";

        /// <summary>Gets or sets the host of <see cref="ProxyEndpoint"/> when <see cref="Endpoint"/> uses an IP
        /// address.</summary>
        /// <value>The host or IP address of <see cref="ProxyEndpoint"/>. Its default value is
        /// <see cref="Dns.GetHostName()"/>.</value>
        public string ProxyHost
        {
            get => _proxyHost;
            set
            {
                if (_listening)
                {
                    throw new InvalidOperationException(
                        "cannot change the proxy host of a server after calling Listen");
                }

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

        internal CancellationToken CancelDispatch => _cancelDispatchSource.Token;

        internal ILogger Logger => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");

        private readonly CancellationTokenSource _cancelDispatchSource = new();

        private Endpoint? _endpoint;

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private IncomingConnectionFactory? _incomingColocConnectionFactory;
        private IncomingConnectionFactory? _incomingConnectionFactory;
        private bool _listening;

        // protects _shutdownTask
        private readonly object _mutex = new();

        private Endpoint? _proxyEndpoint;

        private string _proxyHost = Dns.GetHostName().ToLowerInvariant();

        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Lazy<Task>? _shutdownTask;

        /// <summary>Creates an endpointless proxy for a service hosted by this server.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="path">The path of the service.</param>
        /// <returns>A new proxy.</returns>
        public T CreateEndpointlessProxy<T>(string path) where T : class, IServicePrx
        {
            // temporary
            ProxyOptions.Invoker ??= Invoker;

            // TODO: other than path, the only useful info here is Protocol and its encoding. ProxyOptions are not used
            // unless the user gives a connection to this new proxy.

            return Proxy.GetFactory<T>().Create(path,
                                                Protocol,
                                                Protocol.GetEncoding(),
                                                endpoint: null,
                                                altEndpoints: ImmutableList<Endpoint>.Empty,
                                                connection: null,
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
            options.Invoker ??= Invoker;

            if (_proxyEndpoint.IsDatagram && !options.IsOneway)
            {
                options = options.Clone();
                options.IsOneway = true;
            }

            return Proxy.GetFactory<T>().Create(path,
                                                _proxyEndpoint.Protocol,
                                                _proxyEndpoint.Protocol.GetEncoding(),
                                                _proxyEndpoint,
                                                altEndpoints: ImmutableList<Endpoint>.Empty,
                                                connection: null,
                                                options);
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

                if (_endpoint == null)
                {
                    throw new InvalidOperationException("server has no endpoint");
                }

                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                _incomingConnectionFactory = _endpoint.IsDatagram ?
                    new DatagramIncomingConnectionFactory(this, _endpoint) :
                    new AcceptorIncomingConnectionFactory(this, _endpoint);

                _endpoint = _incomingConnectionFactory.Endpoint;
                UpdateProxyEndpoint();

                if (ConnectionOptions.AuthenticationOptions != null)
                {
                    ConnectionOptions.AuthenticationOptions = ConnectionOptions.AuthenticationOptions.Clone();
                    ConnectionOptions.AuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                    {
                        new SslApplicationProtocol(_endpoint.Protocol.GetName())
                    };
                }

                _incomingConnectionFactory.Activate();

                _listening = true;

                if (HasColocEndpoint && _endpoint.Transport != Transport.Coloc && !_endpoint.IsDatagram)
                {
                    var colocEndpoint = new ColocEndpoint(host: $"{_endpoint.Host}.{_endpoint.TransportName}",
                                                          port: _endpoint.Port,
                                                          protocol: _endpoint.Protocol);

                    _incomingColocConnectionFactory = new AcceptorIncomingConnectionFactory(this, colocEndpoint);
                    _incomingColocConnectionFactory.Activate();
                    EndpointExtensions.RegisterColocEndpoint(_endpoint, colocEndpoint);
                    if (_proxyEndpoint != _endpoint)
                    {
                        EndpointExtensions.RegisterColocEndpoint(_proxyEndpoint!, colocEndpoint);
                    }
                }

                // TODO: remove
                if ((Invoker as Communicator)?.GetPropertyAsBool("Ice.PrintAdapterReady") ?? false)
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
                        if (_proxyEndpoint != _endpoint)
                        {
                            EndpointExtensions.UnregisterColocEndpoint(_proxyEndpoint!);
                        }
                    }

                    // Shuts down the incoming connection factory to stop accepting new incoming requests or
                    // connections. This ensures that once ShutdownAsync returns, no new requests will be dispatched.
                    // Once _shutdownTask is non null, _incomingConnectionfactory cannot change, so no need to lock
                    // _mutex.
                    Task colocShutdownTask = _incomingColocConnectionFactory?.ShutdownAsync() ?? Task.CompletedTask;
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
        public override string ToString() => _endpoint?.ToString() ?? "";

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await ShutdownAsync(new CancellationToken(canceled: true)).ConfigureAwait(false);
            _cancelDispatchSource.Dispose();
        }

        private void UpdateProxyEndpoint() => _proxyEndpoint = _endpoint?.GetProxyEndpoint(ProxyHost);
    }
}
