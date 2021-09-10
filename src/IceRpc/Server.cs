// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
    /// the corresponding responses. A server should be first configured through its properties, then activated with
    /// <see cref="Listen"/> and finally shut down with <see cref="ShutdownAsync"/>.</summary>
    public sealed class Server : IAsyncDisposable
    {
        /// <summary>The default value for <see cref="ServerTransport"/>.</summary>
        public static IServerTransport DefaultServerTransport { get; } =
            new ServerTransport().UseTcp().UseColoc();

        /// <summary>Gets or sets the options of server connections created by this server.</summary>
        public ConnectionOptions ConnectionOptions { get; set; } = new();

        /// <summary>Gets or sets the dispatcher of this server.</summary>
        /// <value>The dispatcher of this server.</value>
        /// <seealso cref="IDispatcher"/>
        /// <seealso cref="Configure.Router"/>
        public IDispatcher? Dispatcher { get; set; }

        /// <summary>Gets or sets the endpoint of this server.</summary>
        /// <value>The endpoint of this server, by default <c>ice+tcp://[::0]</c>.The endpoint's host is usually an
        /// IP address, and it cannot be a DNS name.</value>
        public Endpoint Endpoint
        {
            get => _endpoint;
            set
            {
                if (_listening)
                {
                    throw new InvalidOperationException("cannot change the endpoint of a server after calling Listen");
                }

                _endpoint = value;
            }
        }

        /// <summary>Gets or sets the logger factory of this server. When null, the server creates its logger using
        /// <see cref="NullLoggerFactory.Instance"/>.</summary>
        /// <value>The logger factory of this server.</value>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set
            {
                _loggerFactory = value;
                _logger = _loggerFactory?.CreateLogger("IceRpc") ?? NullLogger.Instance;
            }
        }

        /// <summary>Gets the Ice protocol used by this server.</summary>
        /// <value>The Ice protocol of this server.</value>
        public Protocol Protocol => _endpoint?.Protocol ?? Protocol.Ice2;

        /// <summary>The <see cref="IServerTransport"/> used by this server to accept connections.</summary>
        public IServerTransport ServerTransport { get; set; } = DefaultServerTransport;

        /// <summary>Returns a task that completes when the server's shutdown is complete: see
        /// <see cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        private readonly HashSet<Connection> _connections = new();

        private Endpoint _endpoint = "ice+tcp://[::0]";

        private IListener? _listener;

        private ILogger _logger = NullLogger.Instance;
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

                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                NetworkSocketConnection? networkSocketConnection;
                (_listener, networkSocketConnection) = ServerTransport.Listen(
                    _endpoint,
                    _loggerFactory ?? NullLoggerFactory.Instance);

                if (_listener != null)
                {
                    Debug.Assert(networkSocketConnection == null);
                    _endpoint = _listener.Endpoint;

                    // Run task to start accepting new connections.
                    Task.Run(() => AcceptAsync(_listener));
                }
                else
                {
                    Debug.Assert(networkSocketConnection != null);

                    // Dispose objects before losing scope, the connection is disposed from ShutdownAsync.
#pragma warning disable CA2000
                    var serverConnection = new Connection(
                        networkSocketConnection,
                        Dispatcher,
                        ConnectionOptions,
                        LoggerFactory);
#pragma warning restore CA2000
                    _endpoint = networkSocketConnection.LocalEndpoint!;

                    // Connect the connection to start accepting new streams.
                    _ = serverConnection.ConnectAsync(default);
                    _connections.Add(serverConnection);
                }

                _listening = true;
                _logger.LogServerListening(this);
            }
        }

        /// <summary>Shuts down this server: the server stops accepting new connections and requests, waits for all
        /// outstanding dispatches to complete and gracefully closes all its connections. Once shut down, a server is
        /// disposed and can no longer be used. This method can be safely called multiple times, including from multiple
        /// threads.</summary>
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
                // Yield to ensure _mutex is released while we perform the shutdown.
                await Task.Yield();

                CancellationToken cancel = _shutdownCancelSource!.Token;
                try
                {
                    _logger.LogServerShuttingDown(this);

                    // Stop accepting new connections by disposing of the listeners.
                    _listener?.Dispose();

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
                    // TODO: if _listening = false, this will log shutdown completed even though the server never
                    // actually listened.
                    _logger.LogServerShutdownComplete(this);

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

        private async Task AcceptAsync(IListener listener)
        {
            using IDisposable? scope = _logger.StartServerScope(listener);
            _logger.LogStartAcceptingConnections();

            while (true)
            {
                ITransportConnection transportConnection;
                try
                {
                    transportConnection = await listener.AcceptAsync().ConfigureAwait(false);
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

                    _logger.LogAcceptingConnectionFailed(ex);

                    // We wait for one second to avoid running in a tight loop in case the failures occurs immediately
                    // again. Failures here are unexpected and could be considered fatal.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                // Dispose objects before losing scope, the connection is disposed from ShutdownAsync.
#pragma warning disable CA2000
                var connection = new Connection(
                        transportConnection,
                        Dispatcher,
                        ConnectionOptions,
                        LoggerFactory);
#pragma warning restore CA2000

                lock (_mutex)
                {
                    if (_shutdownTask != null)
                    {
                        connection.CloseAsync("server shutdown");
                        return;
                    }

                    // Add the connection to _connections and setup the callback to remove it when it's closed.
                    _connections.Add(connection);
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

                // We don't wait for the connection to be activated. This could take a while for some transports
                // such as TLS based transports where the handshake requires few round trips between the client
                // and server. Waiting could also cause a security issue if the client doesn't respond to the
                // connection initialization as we wouldn't be able to accept new connections in the meantime.
                _ = connection.ConnectAsync(default);
            }
        }
    }
}
