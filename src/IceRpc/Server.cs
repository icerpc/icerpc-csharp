// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Net;
using System.Net.Security;

namespace IceRpc;

/// <summary>A server serves clients by listening for the requests they send, processing these requests and sending the
/// corresponding responses.</summary>
public sealed class Server : IAsyncDisposable
{
    /// <summary>Gets the server address of this server.</summary>
    /// <value>The server address of this server. Its <see cref="ServerAddress.Transport"/> property is always non-null.
    /// When the address's host is an IP address and the port is 0, <see cref="ListenAsync"/> replaces the port by the
    /// actual port the server is listening on.</value>
    public ServerAddress ServerAddress => _listener.ServerAddress;

    /// <summary>Gets a task that completes when the server's shutdown is complete: see
    /// <see cref="ShutdownAsync(string, CancellationToken)"/> This property can be retrieved before shutdown is
    /// initiated.</summary>
    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private readonly HashSet<IProtocolConnection> _refusedConnections = new();

    private readonly HashSet<IProtocolConnection> _connections = new();

    private bool _listening;

    private readonly IProtocolListener _listener;

    private Task? _listenTask;

    private readonly int _maxConnections;

    // protects _listening, _connections, and _refusedConnections
    private readonly object _mutex = new();

    private readonly ServerAddress _serverAddress;

    private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default"/>.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default"/>.</param>
    public Server(
        ServerOptions options,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null)
    {
        if (options.ConnectionOptions.Dispatcher is null)
        {
            throw new ArgumentException($"{nameof(ServerOptions.ConnectionOptions.Dispatcher)} cannot be null");
        }

        _serverAddress = options.ServerAddress;
        duplexServerTransport ??= IDuplexServerTransport.Default;
        multiplexedServerTransport ??= IMultiplexedServerTransport.Default;
        _maxConnections = options.MaxConnections;

        if (_serverAddress.Transport is null)
        {
            _serverAddress = ServerAddress with
            {
                Transport = _serverAddress.Protocol == Protocol.Ice ?
                    duplexServerTransport.Name : multiplexedServerTransport.Name
            };
        }

        // This is the composition root for the protocol and transport listeners.
        IProtocolListener listener;
        if (_serverAddress.Protocol == Protocol.Ice)
        {
            IListener<IDuplexConnection> transportListener = duplexServerTransport.CreateListener(
                _serverAddress,
                new DuplexConnectionOptions
                {
                    MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                    Pool = options.ConnectionOptions.Pool,
                },
                options.ServerAuthenticationOptions);
            listener = new IceProtocolListener(options.ConnectionOptions, transportListener);
        }
        else
        {
            IListener<IMultiplexedConnection> transportListener = multiplexedServerTransport.CreateListener(
                _serverAddress,
                new MultiplexedConnectionOptions
                {
                    MaxBidirectionalStreams = options.ConnectionOptions.MaxIceRpcBidirectionalStreams,
                    // Add an additional stream for the icerpc protocol control stream.
                    MaxUnidirectionalStreams = options.ConnectionOptions.MaxIceRpcUnidirectionalStreams + 1,
                    MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                    Pool = options.ConnectionOptions.Pool,
                    StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter
                },
                options.ServerAuthenticationOptions);
            listener = new IceRpcProtocolListener(options.ConnectionOptions, transportListener);
        }
        _listener = new LogListenerDecorator(listener);
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

    /// <summary>Constructs a server with the specified dispatcher, server address and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddress">The server address of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(
        IDispatcher dispatcher,
        ServerAddress serverAddress,
        SslServerAuthenticationOptions? authenticationOptions = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                ServerAddress = serverAddress
            })
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address URI and authentication options. All
    /// other properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddressUri">A URI that represents the server address of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(
        IDispatcher dispatcher,
        Uri serverAddressUri,
        SslServerAuthenticationOptions? authenticationOptions = null)
        : this(dispatcher, new ServerAddress(serverAddressUri), authenticationOptions)
    {
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            // We always cancel _shutdownCts with _mutex locked. This way _shutdownCts.Token does not change when
            // _mutex is locked.
            try
            {
                _shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed by a previous or concurrent call.
            }

            // Stop accepting new connections by disposing of the listener
            _listener.Dispose();
        }

        if (_listenTask is not null)
        {
            // Wait for the listen task to complete
            await _listenTask.ConfigureAwait(false);
        }

        await Task.WhenAll(_connections.Union(_refusedConnections).Select(
            c => c.DisposeAsync().AsTask())).ConfigureAwait(false);

        _ = _shutdownCompleteSource.TrySetResult(null);
        _shutdownCts.Dispose();
    }

    /// <summary>Starts listening on the configured server address and dispatching requests from clients.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the server starts accepting new connections. This task can also complete
    /// with a <see cref="TransportException"/> if another server is listening on the same server address.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or shutting
    /// down.</exception>
    public Task ListenAsync(CancellationToken cancellationToken = default)
    {
        CancellationToken shutdownCancellationToken;

        // We lock the mutex because ShutdownAsync can run concurrently.
        lock (_mutex)
        {
            try
            {
                shutdownCancellationToken = _shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException($"{typeof(Server)}");
            }

            if (shutdownCancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"server '{this}' is shut down or shutting down");
            }
            if (_listening)
            {
                throw new InvalidOperationException($"server '{this}' is already listening");
            }
            _listening = true;
        }
        return PerformListenAsync();

        async Task PerformListenAsync()
        {
            // Start listening for new connections.
            await _listener.ListenAsync(cancellationToken).ConfigureAwait(false);

            // Once the listening is started, we can start accepting new connections.
            _listenTask = Task.Run(AcceptConnectionsAsync, CancellationToken.None);
        }

        async Task AcceptConnectionsAsync()
        {
            try
            {
                while (true)
                {
                    (IProtocolConnection connection, _) =
                        await _listener.AcceptAsync(shutdownCancellationToken).ConfigureAwait(false);

                    Func<IProtocolConnection, CancellationToken, Task>? connectionTask;
                    lock (_mutex)
                    {
                        // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                        if (shutdownCancellationToken.IsCancellationRequested)
                        {
                            connectionTask = null;
                        }
                        else if (_maxConnections > 0 && _connections.Count == _maxConnections)
                        {
                            // We have too many connections and can't accept any more.
                            // Reject the underlying transport connection by ShuttingDown the protocol connection.
                            _refusedConnections.Add(connection);
                            connectionTask = RefuseConnectionAsync;
                        }
                        else
                        {
                            _connections.Add(connection);
                            connectionTask = ConnectConnectionAsync;
                        }
                    }

                    if (connectionTask is null)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        // We don't wait for the connection to be activated or shutdown. This could take a while for
                        // some transports such as TLS based transports where the handshake requires few round trips
                        // between the client and server. Waiting could also cause a security issue if the client
                        // doesn't respond to the connection initialization as we wouldn't be able to accept new
                        // connections in the meantime. The call will eventually timeout if the ConnectTimeout expires.
                        _ = Task.Run(
                            () => _ = connectionTask(connection, shutdownCancellationToken),
                            CancellationToken.None);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // The AcceptAsync call can fail with ObjectDisposedException during shutdown once the listener is
                // disposed.
            }
            catch (OperationCanceledException)
            {
                // The AcceptAsync call can fail with OperationCanceledException during shutdown once the shutdown
                // cancellation token is canceled.
            }
            catch (Exception exception)
            {
                ServerEventSource.Log.ConnectionAcceptFailure(ServerAddress, exception);
                _shutdownCompleteSource.TrySetException(exception);
            }
        }

        async Task ConnectConnectionAsync(IProtocolConnection connection, CancellationToken shutdownCancellationToken)
        {
            // Schedule removal after addition, outside mutex lock.
            _ = RemoveFromCollectionAsync(connection, shutdownCancellationToken);

            // Connect the connection.
            await connection.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        async Task RefuseConnectionAsync(IProtocolConnection connection, CancellationToken shutdownCancellationToken)
        {
            try
            {
                await connection.ShutdownAsync(
                    "connection refused: server has too many connections",
                    shutdownCancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore and continue
            }

            lock (_mutex)
            {
                // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // Server.DisposeAsync is responsible to dispose this connection.
                    return;
                }
                else
                {
                    _ = _refusedConnections.Remove(connection);
                }
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }

        // Remove the connection from _connections once shutdown completes
        async Task RemoveFromCollectionAsync(
            IProtocolConnection connection,
            CancellationToken shutdownCancellationToken)
        {
            try
            {
                _ = await connection.ShutdownComplete.WaitAsync(shutdownCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == shutdownCancellationToken)
            {
                // The server is being shut down or disposed and server's DisposeAsync is responsible to DisposeAsync
                // this connection.
                return;
            }
            catch
            {
                // ignore and continue: the connection was aborted
            }

            lock (_mutex)
            {
                // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // Server.DisposeAsync is responsible to dispose this connection.
                    return;
                }
                else
                {
                    _ = _connections.Remove(connection);
                }
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Shuts down this server: the server stops accepting new connections and shuts down gracefully all its
    /// existing connections using a default message.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        ShutdownAsync("Server shutdown", cancellationToken);

    /// <summary>Shuts down this server: the server stops accepting new connections and shuts down gracefully all its
    /// existing connections.</summary>
    /// <param name="message">The message transmitted to the clients with the icerpc protocol.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public async Task ShutdownAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_mutex)
            {
                // We always cancel _shutdownCts with _mutex lock. This way, when _mutex is locked, _shutdownCts.Token
                // does not change.
                try
                {
                    _shutdownCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    throw new ObjectDisposedException($"{typeof(Server)}");
                }

                // Stop accepting new connections by disposing of the listener.
                _listener?.Dispose();
            }

            await Task.WhenAll(_connections.Select(entry => entry.ShutdownAsync(message, cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
            // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
            // using Result or Wait()), ShutdownAsync will complete.
            _ = _shutdownCompleteSource.TrySetResult(null);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => ServerAddress.ToString();

    /// <summary>Provides a decorator that adds logging to a <see cref="IListener{T}"/> of
    /// <see cref="IProtocolConnection"/>.</summary>
    private class LogListenerDecorator : IProtocolListener
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IProtocolListener _decoratee;

        public async Task<(IProtocolConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IProtocolConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

            // We don't log AcceptAsync exceptions; they usually occur when the server is shutting down.
            ServerEventSource.Log.ConnectionStart(ServerAddress, remoteNetworkAddress);
            return (new LogProtocolConnectionDecorator(connection, remoteNetworkAddress), remoteNetworkAddress);
        }

        public void Dispose() => _decoratee.Dispose();

        public Task ListenAsync(CancellationToken cancellationToken) => _decoratee.ListenAsync(cancellationToken);

        internal LogListenerDecorator(IProtocolListener decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds EventSource-based logging to the <see cref="IProtocolConnection"/>.
    /// </summary>
    private class LogProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task<string> ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly IProtocolConnection _decoratee;
        private readonly Task _logShutdownTask;
        private readonly EndPoint _remoteNetworkAddress;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ServerEventSource.Log.ConnectStart(ServerAddress, _remoteNetworkAddress);
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                ServerEventSource.Log.ConnectSuccess(ServerAddress, _remoteNetworkAddress);
                return result;
            }
            catch (Exception exception)
            {
                ServerEventSource.Log.ConnectFailure(ServerAddress, _remoteNetworkAddress, exception);
                throw;
            }
            finally
            {
                ServerEventSource.Log.ConnectStop(ServerAddress, _remoteNetworkAddress);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            await _logShutdownTask.ConfigureAwait(false);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public Task ShutdownAsync(string message, CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(message, cancellationToken);

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, EndPoint remoteNetworkAddress)
        {
            _decoratee = decoratee;
            _remoteNetworkAddress = remoteNetworkAddress;

            _logShutdownTask = LogShutdownAsync();

            // This task executes once per decorated connection.
            async Task LogShutdownAsync()
            {
                try
                {
                    string message = await ShutdownComplete.ConfigureAwait(false);
                    ServerEventSource.Log.ConnectionShutdown(ServerAddress, remoteNetworkAddress, message);
                }
                catch (Exception exception)
                {
                    ServerEventSource.Log.ConnectionFailure(
                        ServerAddress,
                        remoteNetworkAddress,
                        exception);
                }
                ServerEventSource.Log.ConnectionStop(ServerAddress, _remoteNetworkAddress);
            }
        }
    }
}
