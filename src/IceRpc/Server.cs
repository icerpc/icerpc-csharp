// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net.Security;

namespace IceRpc;

/// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
/// the corresponding responses. A server should be first configured through its properties, then activated with
/// <see cref="Listen"/> and finally shut down with <see cref="ShutdownAsync"/>.</summary>
public sealed class Server : IAsyncDisposable
{
    /// <summary>Gets the default server transport for icerpc protocol connections.</summary>
    public static IServerTransport<IMultiplexedNetworkConnection> DefaultMultiplexedServerTransport { get; } =
        new SlicServerTransport(new TcpServerTransport());

    /// <summary>Gets the default server transport for ice protocol connections.</summary>
    public static IServerTransport<ISimpleNetworkConnection> DefaultSimpleServerTransport { get; } =
        new TcpServerTransport();

    /// <summary>Gets the server's endpoint.</summary>
    /// <value>The endpoint of this server. Once <see cref="Listen"/> is called, the endpoint's value is the
    /// listening endpoint returned by the transport.</value>
    public Endpoint Endpoint { get; private set; }

    /// <summary>Gets a task that completes when the server's shutdown is complete: see <see
    /// cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated.</summary>
    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private readonly HashSet<ServerConnection> _connections = new();

    private bool _isDisposed;

    private bool _isReadOnly;

    private IListener? _listener;

    private bool _listening;

    private readonly ILoggerFactory _loggerFactory;
    private readonly IServerTransport<IMultiplexedNetworkConnection> _multiplexedServerTransport;
    private readonly IServerTransport<ISimpleNetworkConnection> _simpleServerTransport;

    // protects _shutdownTask
    private readonly object _mutex = new();

    private readonly ServerOptions _options;

    private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections.</param>
    /// <param name="simpleServerTransport">The transport used to create ice protocol connections.</param>
    public Server(
        ServerOptions options,
        ILoggerFactory? loggerFactory = null,
        IServerTransport<IMultiplexedNetworkConnection>? multiplexedServerTransport = null,
        IServerTransport<ISimpleNetworkConnection>? simpleServerTransport = null)
    {
        Endpoint = options.Endpoint;
        _options = options;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _multiplexedServerTransport = multiplexedServerTransport ?? DefaultMultiplexedServerTransport;
        _simpleServerTransport = simpleServerTransport ?? DefaultSimpleServerTransport;
    }

    /// <summary>Constructs a server with the specified dispatcher and authentication options. All other properties
    /// have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(IDispatcher dispatcher, SslServerAuthenticationOptions? authenticationOptions = null)
        : this(new ServerOptions
        {
            ServerAuthenticationOptions = authenticationOptions,
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
    /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(
        IDispatcher dispatcher,
        Endpoint endpoint,
        ILoggerFactory? loggerFactory = null,
        SslServerAuthenticationOptions? authenticationOptions = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                Endpoint = endpoint
            },
            loggerFactory)
    {
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _isReadOnly = true;
        }

        try
        {
            await Task.WhenAll(_connections.Select(connection => connection.DisposeAsync().AsTask()))
                .ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
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
            ThrowIfDisposed();

            if (_listening)
            {
                throw new InvalidOperationException($"server '{this}' is already listening");
            }
            if (_isReadOnly)
            {
                throw new InvalidOperationException($"server '{this}' is shutting down");
            }

            if (_options.Endpoint.Protocol == Protocol.Ice)
            {
                PerformListen(
                    _simpleServerTransport,
                    (networkConnection, options) => new IceProtocolConnection(networkConnection, options),
                    LogSimpleNetworkConnectionDecorator.Decorate);
            }
            else
            {
                PerformListen(
                    _multiplexedServerTransport,
                    (networkConnection, options) => new IceRpcProtocolConnection(networkConnection, options),
                    LogMultiplexedNetworkConnectionDecorator.Decorate);
            }

            _listening = true;
        }

        void PerformListen<T>(
            IServerTransport<T> serverTransport,
            Func<T, ConnectionOptions, IProtocolConnection> protocolConnectionFactory,
            LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory)
                where T : INetworkConnection
        {
            // This is the composition root of Server, where we install log decorators when logging is enabled.

            ILogger logger = _loggerFactory.CreateLogger("IceRpc.Server");

            IListener<T> listener = serverTransport.Listen(Endpoint, _options.ServerAuthenticationOptions, logger);
            _listener = listener;
            Endpoint = listener.Endpoint;

            // TODO: log level
            if (logger.IsEnabled(LogLevel.Error))
            {
                listener = new LogListenerDecorator<T>(listener, logger, logDecoratorFactory);
                _listener = listener;

                Func<T, ConnectionOptions, IProtocolConnection> decoratee = protocolConnectionFactory;
                protocolConnectionFactory = (T networkConnection, ConnectionOptions options) =>
                    new LogProtocolConnectionDecorator(decoratee(networkConnection, options), logger);
            }

            // Run task to start accepting new connections.
            _ = Task.Run(() => AcceptAsync(listener, protocolConnectionFactory));
        }

        async Task AcceptAsync<T>(
            IListener<T> listener,
            Func<T, ConnectionOptions, IProtocolConnection> protocolConnectionFactory)
                where T : INetworkConnection
        {
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
                        if (_isReadOnly)
                        {
                            return;
                        }
                    }

                    // We wait for one second to avoid running in a tight loop in case the failures occurs
                    // immediately again. Failures here are unexpected and could be considered fatal.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                IProtocolConnection protocolConnection = protocolConnectionFactory(
                    networkConnection,
                    _options.ConnectionOptions);

                // Dispose objects before losing scope, the connection is disposed from ShutdownAsync.
#pragma warning disable CA2000
                var connection = new ServerConnection(protocolConnection, _options.ConnectionOptions);
#pragma warning restore CA2000

                lock (_mutex)
                {
                    if (_isReadOnly)
                    {
                        connection.Abort();
                        return;
                    }

                    _ = _connections.Add(connection);
                }

                // Schedule removal after addition. We do this outside the mutex lock otherwise
                // await serverConnection.ShutdownAsync could be called within this lock.
                connection.OnClose(exception => _ = RemoveFromCollectionAsync(connection));

                // We don't wait for the connection to be activated. This could take a while for some transports
                // such as TLS based transports where the handshake requires few round trips between the client
                // and server. Waiting could also cause a security issue if the client doesn't respond to the
                // connection initialization as we wouldn't be able to accept new connections in the meantime.
                _ = connection.ConnectAsync();
            }
        }

        // Remove the connection from _connections once shutdown completes
        async Task RemoveFromCollectionAsync(ServerConnection connection)
        {
            lock (_mutex)
            {
                // the _connections collection is read-only when disposed or shutting down
                if (_isReadOnly)
                {
                    // We're done, the connection shutdown is taken care of by the shutdown task.
                    return;
                }

                bool removed = _connections.Remove(connection);
                Debug.Assert(removed);
            }

            // TODO: don't initiate shutdown that waits forever
            await connection.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
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
        try
        {
            lock (_mutex)
            {
                ThrowIfDisposed();

                _isReadOnly = true;

                // Stop accepting new connections by disposing of the listener.
                _listener?.Dispose();
                _listener = null;
            }

            // Shuts down the connections to stop accepting new incoming requests. This ensures that once
            // ShutdownAsync returns, no new requests will be dispatched. ShutdownAsync on each connection waits
            // for the connection dispatch to complete. If the cancellation token is canceled, the dispatch will
            // be canceled. This can speed up the shutdown if the dispatch check the dispatch cancellation
            // token.
            await Task.WhenAll(
                _connections.Select(
                    connection => connection.ShutdownAsync(
                        "server shutdown",
                        cancelDispatches: false,
                        cancelInvocations: false,
                        cancel))).ConfigureAwait(false);
        }
        finally
        {
            // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
            // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
            // using Result or Wait()), ShutdownAsync will complete.
            _shutdownCompleteSource.TrySetResult(null);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Endpoint.ToString();

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(Server)}:{this}");
        }
    }
}
