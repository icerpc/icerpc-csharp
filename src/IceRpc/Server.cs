// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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

        /// <summary>Gets or sets the options of server connections created by this server.</summary>
        public ServerConnectionOptions ConnectionOptions { get; set; } = new();

        /// <summary>Gets or sets the dispatcher of this server.</summary>
        /// <value>The dispatcher of this server.</value>
        /// <seealso cref="IDispatcher"/>
        /// <seealso cref="Router"/>
        public IDispatcher? Dispatcher { get; set; }

        /// <summary>Gets or sets the endpoint of this server.</summary>
        /// <value>The endpoint of this server, for example <c>ice+tcp://[::0]</c>.The endpoint's host is usually an
        /// IP address, and it cannot be a DNS name.</value>
        public Endpoint? Endpoint
        {
            get => _endpoint;
            set
            {
                if (_listening)
                {
                    throw new InvalidOperationException("cannot change the endpoint of a server after calling Listen");
                }

                _endpoint = value;
                UpdateProxyEndpoint();
            }
        }

        /// <summary>Gets or sets whether this server listens on an endpoint for the coloc transport in addition to its
        /// regular endpoint. This property has no effect when <see cref="Endpoint"/>'s transport is coloc. Changing
        /// this value after calling <see cref="Listen"/> has no effect as well.</summary>
        /// <value>True when the server listens on an endpoint for the coloc transport; otherwise, false. The default
        /// value is true.</value>
        public bool HasColocEndpoint { get; set; } = true;

        /// <summary>Gets or sets the host of <see cref="ProxyEndpoint"/> when <see cref="Endpoint"/> uses an IP
        /// address.</summary>
        /// <value>The host or IP address of <see cref="ProxyEndpoint"/>. Its default value is
        /// <see cref="Dns.GetHostName()"/>.</value>
        public string HostName
        {
            get => _hostName;
            set
            {
                if (value.Length == 0)
                {
                    throw new ArgumentException($"{nameof(HostName)} must have at least one character",
                                                nameof(HostName));
                }
                _hostName = value;
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

        /// <summary>Gets the Ice protocol used by this server.</summary>
        /// <value>The Ice protocol of this server.</value>
        public Protocol Protocol => _endpoint?.Protocol ?? Protocol.Ice2;

        /// <summary>Returns the endpoint included in proxies created by
        /// <see cref="IServicePrx.FromServer(Server, string?)"/>. This endpoint is computed from the values of
        /// <see cref="Endpoint"/> and <see cref="HostName"/>.</summary>
        /// <value>An endpoint when <see cref="Endpoint"/> is not null; otherwise, null.</value>
        public Endpoint? ProxyEndpoint { get; private set; }

        /// <summary>Returns a task that completes when the server's shutdown is complete: see
        /// <see cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        internal ILogger Logger => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");

        /// <summary>Dictionary of non-coloc endpoint to coloc endpoint used by GetColocCounterPart.</summary>
        private static readonly IDictionary<Endpoint, ColocEndpoint> _colocRegistry =
            new ConcurrentDictionary<Endpoint, ColocEndpoint>(EndpointComparer.Equivalent);

        private IListener? _colocListener;

        private readonly HashSet<Connection> _connections = new();

        private Endpoint? _endpoint;

        private string _hostName = Dns.GetHostName().ToLowerInvariant();

        private IListener? _listener;

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private bool _listening;

        // protects _shutdownTask
        private readonly object _mutex = new();

        private CancellationTokenSource? _shutdownCancelSource;

        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? _shutdownTask;

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

                if (_endpoint.TransportDescriptor?.ListenerFactory is
                    Func<Endpoint, ServerConnectionOptions, ILogger, IListener> listenerFactory)
                {
                    _listener = listenerFactory(_endpoint, ConnectionOptions, Logger);
                    _endpoint = _listener.Endpoint;
                    UpdateProxyEndpoint();

                    // Run task to start accepting new connections.
                    Task.Run(() => AcceptAsync(_listener));
                }
                else if (_endpoint.TransportDescriptor?.ServerConnectionFactory is
                    Func<Endpoint, ServerConnectionOptions, ILogger, MultiStreamConnection> serverConnectionFactory)
                {
                    MultiStreamConnection multiStreamConnection =
                        serverConnectionFactory(_endpoint, ConnectionOptions, Logger);
                    var serverConnection = new Connection(multiStreamConnection, this);
                    _endpoint = multiStreamConnection.LocalEndpoint!;
                    UpdateProxyEndpoint();

                    // Connect the connection to start accepting new streams.
                    _ = serverConnection.ConnectAsync(default);
                    _connections.Add(serverConnection);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"cannot create listener or server connection with endpoint '{_endpoint}'");
                }

                _listening = true;

                if (HasColocEndpoint && _endpoint.Transport != Transport.Coloc && !_endpoint.IsDatagram)
                {
                    var colocEndpoint = new ColocEndpoint(host: $"{_endpoint.Host}.{_endpoint.TransportName}",
                                                          port: _endpoint.Port,
                                                          protocol: _endpoint.Protocol);

                    _colocListener =
                        colocEndpoint.TransportDescriptor!.ListenerFactory!(colocEndpoint, ConnectionOptions, Logger);
                    Task.Run(() => AcceptAsync(_colocListener));

                    _colocRegistry.Add(_endpoint, colocEndpoint);
                    if (ProxyEndpoint != _endpoint)
                    {
                        _colocRegistry.Add(ProxyEndpoint!, colocEndpoint);
                    }
                }

                Logger.LogServerListening(this);
            }
        }

        /// <summary>Shuts down this server: the server stops accepting new connections and requests, waits for all
        /// outstanding dispatches to complete and gracefully closes all its server connections. Once shut down, a
        /// server is disposed and can no longer be used. This method can be safely called multiple times, including
        /// from multiple threads.</summary>
        /// <param name="cancel">The cancellation token. When this token is canceled, the cancellation token of all
        /// outstanding dispatches is canceled, which can speed up the shutdown provided the operation implementations
        /// check their cancellation tokens.</param>
        /// <return>A task that completes once the shutdown is complete.</return>
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

            // Wait for shutdown to complete.
            await _shutdownTask.ConfigureAwait(false);

            async Task PerformShutdownAsync()
            {
                CancellationToken cancel = _shutdownCancelSource!.Token;
                try
                {
                    Logger.LogServerShuttingDown(this);

                    // No longer available for coloc connections (may not be registered at all)
                    if (_endpoint is Endpoint endpoint && endpoint.Transport != Transport.Coloc)
                    {
                        _colocRegistry.Remove(endpoint);
                        if (ProxyEndpoint != _endpoint)
                        {
                            _colocRegistry.Remove(ProxyEndpoint!);
                        }
                    }

                    // Stop accepting new connections by disposing of the listeners.
                    _listener?.Dispose();
                    _colocListener?.Dispose();

                    // Yield to ensure the mutex is released while we shutdown the connections.
                    await Task.Yield();

                    // Shuts down the connections to stop accepting new incoming requests. This ensures that
                    // once ShutdownAsync returns, no new requests will be dispatched. ShutdownAsync on each
                    // connections waits for the connection dispatch to complete. If the cancellation token
                    // is canceled, the dispatch will be cancelled. This can speed up the shutdown if the
                    // dispatch check the dispatch cancellation token.
                    await Task.WhenAll(_connections.Select(
                        connection => connection.ShutdownAsync("server shutdown", cancel))).ConfigureAwait(false);
                }
                finally
                {
                    Logger.LogServerShutdownComplete(this);

                    _shutdownCancelSource!.Dispose();

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
        public async ValueTask DisposeAsync() =>
            await ShutdownAsync(new CancellationToken(canceled: true)).ConfigureAwait(false);

        /// <summary>Returns the corresponding endpoint for the coloc transport, if there is one.</summary>
        /// <param name="endpoint">The endpoint to check.</param>
        /// <returns>The corresponding endpoint for the coloc transport, or null if there is no such endpoint</returns>
        internal static Endpoint? GetColocEndpoint(Endpoint endpoint) =>
            endpoint.Transport == Transport.Coloc ? endpoint :
                (_colocRegistry.TryGetValue(endpoint, out ColocEndpoint? colocEndpoint) ? colocEndpoint : null);

        private void UpdateProxyEndpoint() => ProxyEndpoint = _endpoint?.GetProxyEndpoint(HostName);

        private async Task AcceptAsync(IListener listener)
        {
            using IDisposable? scope = Logger.StartListenerScope(this, listener);
            Logger.LogStartAcceptingConnections();

            while (true)
            {
                MultiStreamConnection multiStreamConnection;
                try
                {
                    multiStreamConnection = await listener.AcceptAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lock (_mutex)
                    {
                        if (_shutdownTask != null)
                        {
                            return;
                        }
                    }

                    Logger.LogAcceptingConnectionFailed(ex);

                    // We wait for one second to avoid running in a tight loop in case the failures occurs immediately
                    // again. Failures here are unexpected and could be considered fatal.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                var connection = new Connection(multiStreamConnection, this);

                lock (_mutex)
                {
                    if (_shutdownTask != null)
                    {
                        connection.AbortAsync("server shutdown");
                        return;
                    }

                    _connections.Add(connection);

                    // We don't wait for the connection to be activated. This could take a while for some transports
                    // such as TLS based transports where the handshake requires few round trips between the client
                    // and server. Waiting could also cause a security issue if the client doesn't respond to the
                    // connection initialization as we wouldn't be able to accept new connections in the meantime.
                    _ = AcceptConnectionAsync(connection);
                }

                // Set the callback used to remove the connection from the factory.
                connection.Remove = connection =>
                {
                    lock (_mutex)
                    {
                        if (_shutdownTask == null)
                        {
                            _connections.Remove(connection);
                        }
                    }
                };
            }

            async Task AcceptConnectionAsync(Connection connection)
            {
                using var source = new CancellationTokenSource(ConnectionOptions.AcceptTimeout);
                CancellationToken cancel = source.Token;
                try
                {
                    // Connect the connection (handshake, protocol initialization, ...)
                    await connection.ConnectAsync(cancel).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }
}
