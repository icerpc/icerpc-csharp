// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc
{
    /// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
    /// the corresponding responses. A server should be first configured through its properties, then activated with
    /// <see cref="Listen"/> and finally shut down with <see cref="ShutdownAsync"/>.</summary>
    public sealed class Server : IAsyncDisposable
    {
        private static readonly IServerTransport<IMultiplexedNetworkConnection> _defaultMultiplexedTransport =
            new SlicServerTransport(new TcpServerTransport());

        private static readonly IServerTransport<ISimpleNetworkConnection> _defaultSimleTransport =
            new TcpServerTransport();

        /// <summary>Returns the endpoint of this server.</summary>
        /// <value>The endpoint of this server. Once <see cref="Listen"/> is called, the endpoint's value is the
        /// listening endpoint returned by the transport.</value>
        public Endpoint Endpoint { get; private set; }

        /// <summary>Returns a task that completes when the server's shutdown is complete: see <see
        /// cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        private readonly HashSet<Connection> _connections = new();

        private IListener? _listener;

        private bool _listening;

        private readonly ILoggerFactory _loggerFactory;
        private readonly IServerTransport<IMultiplexedNetworkConnection> _multiplexedTransport;
        private readonly IServerTransport<ISimpleNetworkConnection> _simpleTransport;

        // protects _shutdownTask
        private readonly object _mutex = new();

        private readonly ServerOptions _options;

        private CancellationTokenSource? _shutdownCancelSource;

        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? _shutdownTask;

        /// <summary>Constructs a server.</summary>
        /// <param name="options">The server options.</param>
        /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
        /// <param name="multiplexedTransport">The transport used to create icerpc protocol connections.</param>
        /// <param name="simpleTransport">The transport used to create ice protocol connections.</param>
        public Server(
            ServerOptions options,
            ILoggerFactory? loggerFactory = null,
            IServerTransport<IMultiplexedNetworkConnection>? multiplexedTransport = null,
            IServerTransport<ISimpleNetworkConnection>? simpleTransport = null)
        {
            Endpoint = options.Endpoint;
            _options = options;
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _multiplexedTransport = multiplexedTransport ?? _defaultMultiplexedTransport;
            _simpleTransport = simpleTransport ?? _defaultSimleTransport;
        }

        /// <summary>Constructs a server with the specified dispatcher and authentication options. All other properties
        /// have their default values.</summary>
        /// <param name="dispatcher">The dispatcher of the server.</param>
        /// <param name="authenticationOptions">The server authentication options.</param>
        public Server(IDispatcher dispatcher, SslServerAuthenticationOptions? authenticationOptions = null)
            : this(new ServerOptions
            {
                AuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                }
            })
        {
        }

        /// <summary>Constructs a server with the specified dispatcher, endpoint and authentication options. All other
        /// properties have their default values.</summary>
        /// <param name="dispatcher">The dispatcher of the server.</param>
        /// <param name="endpoint">The endpoint of the server.</param>
        /// <param name="authenticationOptions">The server authentication options.</param>
        public Server(
            IDispatcher dispatcher,
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions = null)
            : this(new ServerOptions
            {
                AuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                Endpoint = endpoint
            })
        {
        }

        /// <summary>Starts listening on the configured endpoint and dispatching requests from clients. If the
        /// configured endpoint is an IP endpoint with port 0, this method updates the endpoint to include the actual
        /// port selected by the operating system.</summary>
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
                    throw new ObjectDisposedException($"{typeof(Server)}:{this}");
                }

                if (_options.Endpoint.Protocol == Protocol.Ice)
                {
                    PerformListen(
                        _simpleTransport,
                        IceProtocol.Instance.ProtocolConnectionFactory,
                        LogSimpleNetworkConnectionDecorator.Decorate);
                }
                else
                {
                    PerformListen(
                        _multiplexedTransport,
                        IceRpcProtocol.Instance.ProtocolConnectionFactory,
                        LogMultiplexedNetworkConnectionDecorator.Decorate);
                }

                _listening = true;
            }

            void PerformListen<T>(
                IServerTransport<T> serverTransport,
                IProtocolConnectionFactory<T> protocolConnectionFactory,
                LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory)
                    where T : INetworkConnection
            {
                // This is the composition root of Server, where we install log decorators when logging is enabled.

                ILogger logger = _loggerFactory.CreateLogger("IceRpc.Server");

                IListener<T> listener = serverTransport.Listen(Endpoint, _options.AuthenticationOptions, logger);
                _listener = listener;
                Endpoint = listener.Endpoint;

                Action<Connection, Exception>? onClose = null;

                if (logger.IsEnabled(LogLevel.Error)) // TODO: log level
                {
                    listener = new LogListenerDecorator<T>(listener, logger, logDecoratorFactory);
                    _listener = listener;

                    protocolConnectionFactory =
                        new LogProtocolConnectionFactoryDecorator<T>(protocolConnectionFactory, logger);

                    onClose = (connection, exception) =>
                    {
                        if (connection.NetworkConnectionInformation is NetworkConnectionInformation connectionInformation)
                        {
                            using IDisposable scope = logger.StartServerConnectionScope(connectionInformation);
                            logger.LogConnectionClosedReason(exception);
                        }
                    };
                }

                // Run task to start accepting new connections.
                _ = Task.Run(() => AcceptAsync(listener, protocolConnectionFactory, onClose));
            }

            async Task AcceptAsync<T>(
                IListener<T> listener,
                IProtocolConnectionFactory<T> protocolConnectionFactory,
                Action<Connection, Exception>? onClose)
                    where T : INetworkConnection
            {
                // The common connection options, set through ServerOptions.
                var connectionOptions = new ConnectionOptions
                {
                    CloseTimeout = _options.ConnectionOptions.CloseTimeout,
                    ConnectTimeout = _options.ConnectionOptions.ConnectTimeout,
                    Dispatcher = _options.ConnectionOptions.Dispatcher,
                    Features = _options.ConnectionOptions.Features,
                    KeepAlive = _options.ConnectionOptions.KeepAlive,
                    IceMaxConcurrentDispatches = _options.ConnectionOptions.IceMaxConcurrentDispatches,
                    IceMaxFrameSize = _options.ConnectionOptions.IceMaxFrameSize,
                    IceRpcMaxHeaderSize = _options.ConnectionOptions.IceRpcMaxHeaderSize,
                    OnClose = RemoveOnClose + _options.ConnectionOptions.OnClose
                };

                while (true)
                {
                    T networkConnection;
                    try
                    {
                        networkConnection = await listener.AcceptAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        lock (_mutex)
                        {
                            if (_shutdownTask != null)
                            {
                                return;
                            }
                        }

                        // We wait for one second to avoid running in a tight loop in case the failures occurs
                        // immediately again. Failures here are unexpected and could be considered fatal.
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                        continue;
                    }

                    // Dispose objects before losing scope, the connection is disposed from ShutdownAsync.
#pragma warning disable CA2000
                    var connection = new ServerConnection(Endpoint, connectionOptions, _loggerFactory);
#pragma warning restore CA2000

                    lock (_mutex)
                    {
                        if (_shutdownTask != null)
                        {
                            connection.Abort();
                            return;
                        }

                        _ = _connections.Add(connection);
                    }

                    // We don't wait for the connection to be activated. This could take a while for some transports
                    // such as TLS based transports where the handshake requires few round trips between the client
                    // and server. Waiting could also cause a security issue if the client doesn't respond to the
                    // connection initialization as we wouldn't be able to accept new connections in the meantime.
                    _ = connection.ConnectAsync(networkConnection, protocolConnectionFactory, onClose);
                }
            }

            void RemoveOnClose(Connection connection, Exception _)
            {
                lock (_mutex)
                {
                    if (_shutdownTask == null)
                    {
                        _connections.Remove(connection);
                    }
                }
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
                    // Stop accepting new connections by disposing of the listener.
                    if (_listener is IListener listener)
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }

                    // Shuts down the connections to stop accepting new incoming requests. This ensures that once
                    // ShutdownAsync returns, no new requests will be dispatched. ShutdownAsync on each connection waits
                    // for the connection dispatch to complete. If the cancellation token is canceled, the dispatch will
                    // be canceled. This can speed up the shutdown if the dispatch check the dispatch cancellation
                    // token.
                    await Task.WhenAll(_connections.Select(
                        connection => connection.ShutdownAsync("server shutdown", cancel))).ConfigureAwait(false);
                }
                finally
                {
                    _shutdownCancelSource!.Dispose();

                    // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
                    // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
                    // using Result or Wait()), ShutdownAsync will complete.
                    _shutdownCompleteSource.TrySetResult(null);
                }
            }
        }

        /// <inherit-doc/>
        public override string ToString() => Endpoint.ToString();

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => new(ShutdownAsync(new CancellationToken(canceled: true)));
    }
}
