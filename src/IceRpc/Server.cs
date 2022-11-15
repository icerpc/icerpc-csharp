// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Security;

namespace IceRpc;

/// <summary>A server serves clients by listening for the requests they send, processing these requests and sending the
/// corresponding responses.</summary>
public sealed class Server : IAsyncDisposable
{
    /// <summary>Gets the server address of this server.</summary>
    /// <value>The server address of this server. Its <see cref="ServerAddress.Transport" /> property is always
    /// non-null. When the address's host is an IP address and the port is 0, <see cref="Listen" /> replaces the port by
    /// the actual port the server is listening on.</value>
    public ServerAddress ServerAddress => _listener?.ServerAddress ?? _serverAddress;

    /// <summary>Gets a task that completes when the server's shutdown is complete: see
    /// <see cref="ShutdownAsync(CancellationToken)" /> This property can be retrieved before shutdown is initiated.
    /// </summary>
    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private int _backgroundConnectionDisposeCount;

    private readonly TaskCompletionSource _backgroundConnectionDisposeTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly HashSet<IProtocolConnection> _connections = new();

    private readonly IDuplexServerTransport _duplexServerTransport;

    private IListener? _listener;

    // We keep this task to await the completion of listener's DisposeAsync when making concurrent calls to
    // ShutdownAsync and/or DisposeAsync.
    private Task? _listenerDisposeTask;

    private Task? _listenTask;

    private readonly ILogger? _logger;

    private readonly IMultiplexedServerTransport _multiplexedServerTransport;

    // protects _listener and _connections
    private readonly object _mutex = new();

    private readonly ServerOptions _options;

    private readonly ServerAddress _serverAddress;

    private readonly TaskCompletionSource _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The server logger.</param>
    public Server(
        ServerOptions options,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null,
        ILogger? logger = null)
    {
        if (options.ConnectionOptions.Dispatcher is null)
        {
            throw new ArgumentException($"{nameof(ServerOptions.ConnectionOptions.Dispatcher)} cannot be null");
        }
        _serverAddress = options.ServerAddress;
        _duplexServerTransport = duplexServerTransport ?? IDuplexServerTransport.Default;
        _multiplexedServerTransport ??= multiplexedServerTransport ?? IMultiplexedServerTransport.Default;
        _logger = logger;
        _options = options;

        if (_serverAddress.Transport is null)
        {
            _serverAddress = ServerAddress with
            {
                Transport = _serverAddress.Protocol == Protocol.Ice ?
                    _duplexServerTransport.Name : _multiplexedServerTransport.Name
            };
        }
    }

    /// <summary>Constructs a server with the specified dispatcher and authentication options. All other properties
    /// have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAuthenticationOptions">The server authentication options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The server logger.</param>
    public Server(
        IDispatcher dispatcher,
        SslServerAuthenticationOptions? serverAuthenticationOptions = null,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null,
        ILogger? logger = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = serverAuthenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                }
            },
            duplexServerTransport,
            multiplexedServerTransport,
            logger)
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddress">The server address of the server.</param>
    /// <param name="serverAuthenticationOptions">The server authentication options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The server logger.</param>
    public Server(
        IDispatcher dispatcher,
        ServerAddress serverAddress,
        SslServerAuthenticationOptions? serverAuthenticationOptions = null,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null,
        ILogger? logger = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = serverAuthenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                ServerAddress = serverAddress
            },
            duplexServerTransport,
            multiplexedServerTransport,
            logger)
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address URI and authentication options. All
    /// other properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddressUri">A URI that represents the server address of the server.</param>
    /// <param name="serverAuthenticationOptions">The server authentication options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The server logger.</param>
    public Server(
        IDispatcher dispatcher,
        Uri serverAddressUri,
        SslServerAuthenticationOptions? serverAuthenticationOptions = null,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null,
        ILogger? logger = null)
        : this(
            dispatcher,
            new ServerAddress(serverAddressUri),
            serverAuthenticationOptions,
            duplexServerTransport,
            multiplexedServerTransport,
            logger)
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

            if (_backgroundConnectionDisposeCount == 0)
            {
                // There is no outstanding background dispose.
                _ = _backgroundConnectionDisposeTcs.TrySetResult();
            }

            if (_listener is not null)
            {
                // Stop accepting new connections by disposing of the listener.
                _listenerDisposeTask = _listener.DisposeAsync().AsTask();
                _listener = null;
            }
        }

        if (_listenTask is not null)
        {
            // Wait for the listen task to complete
            await _listenTask.ConfigureAwait(false);
        }

        if (_listenerDisposeTask is not null)
        {
            await _listenerDisposeTask.ConfigureAwait(false);
        }

        await Task.WhenAll(_connections.Select(c => c.DisposeAsync().AsTask())
            .Append(_backgroundConnectionDisposeTcs.Task)).ConfigureAwait(false);
        _ = _shutdownCompleteSource.TrySetResult();
        _shutdownCts.Dispose();
    }

    /// <summary>Starts listening on the configured server address and dispatching requests from clients.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or
    /// shutting down.</exception>
    /// <exception cref="TransportException">Thrown when another server is already listening on the same server address.
    /// </exception>
    public void Listen()
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
            if (_listener is not null)
            {
                throw new InvalidOperationException($"server '{this}' is already listening");
            }

            _listenTask = ServerAddress.Protocol == Protocol.Ice ?
                ListenIceConnectionAsync() :
                ListenIceRpcConnectionAsync();

            Debug.Assert(_listener is not null);
        }

        async Task ListenIceConnectionAsync()
        {
            IListener<IDuplexConnection> listener = _duplexServerTransport.Listen(
                _serverAddress,
                new DuplexConnectionOptions
                {
                    MinSegmentSize = _options.ConnectionOptions.MinSegmentSize,
                    Pool = _options.ConnectionOptions.Pool,
                },
                _options.ServerAuthenticationOptions);

            listener = new MetricsDuplexListenerDecorator(listener);
            if (_logger is not null)
            {
                listener = new LogDuplexListenerDecorator(listener, _logger);
            }
            _listener = listener;

            await Task.Yield(); // Ensures that the code below is called outside the server mutex lock.

            try
            {
                while (true)
                {
                    (IDuplexConnection duplexConnection, _) = await listener.AcceptAsync(shutdownCancellationToken)
                        .ConfigureAwait(false);

                    _ = Task.Run(async () =>
                    {
                        TransportConnectionInformation transportConnectionInformation;
                        try
                        {
                            transportConnectionInformation = await duplexConnection.ConnectAsync(
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            duplexConnection.Dispose();
                            return;
                        }

                        IProtocolConnection? protocolConnection = null;
                        lock (_mutex)
                        {
                            // If server is not shutdown and the max connection count is not reached, create the
                            // protocol connection.
                            if (!shutdownCancellationToken.IsCancellationRequested &&
                                (_options.MaxConnections == 0 || _connections.Count < _options.MaxConnections))
                            {
                                protocolConnection = new IceProtocolConnection(
                                    duplexConnection,
                                    transportConnectionInformation,
                                    _options.ConnectionOptions);

                                protocolConnection = new MetricsProtocolConnectionDecorator(protocolConnection);
                                if (_logger is not null)
                                {
                                    protocolConnection = new LogProtocolConnectionDecorator(
                                        protocolConnection,
                                        transportConnectionInformation.LocalNetworkAddress,
                                        _logger);
                                }

                                _connections.Add(protocolConnection);
                            }
                        }

                        if (shutdownCancellationToken.IsCancellationRequested)
                        {
                            // Server is shutdown, dispose the transport connection.
                            duplexConnection.Dispose();
                        }
                        else if (protocolConnection is null)
                        {
                            // The max connection count is reached, refuse the connection.
                            try
                            {
                                await duplexConnection.ShutdownAsync(shutdownCancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                // ignore and continue
                            }

                            duplexConnection.Dispose();
                        }
                        else
                        {
                            await ConnectConnectionAsync(
                                protocolConnection,
                                shutdownCancellationToken).ConfigureAwait(false);
                        }
                    });
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
                _shutdownCompleteSource.TrySetException(exception);
            }
        }

        async Task ListenIceRpcConnectionAsync()
        {
            IListener<IMultiplexedConnection> listener = _multiplexedServerTransport.Listen(
                _serverAddress,
                new MultiplexedConnectionOptions
                {
                    MaxBidirectionalStreams = _options.ConnectionOptions.MaxIceRpcBidirectionalStreams,
                    // Add an additional stream for the icerpc protocol control stream.
                    MaxUnidirectionalStreams = _options.ConnectionOptions.MaxIceRpcUnidirectionalStreams + 1,
                    MinSegmentSize = _options.ConnectionOptions.MinSegmentSize,
                    Pool = _options.ConnectionOptions.Pool
                },
                _options.ServerAuthenticationOptions);

            listener = new MetricsMultiplexedListenerDecorator(listener);
            if (_logger is not null)
            {
                listener = new LogMultiplexedListenerDecorator(listener, _logger);
            }
            _listener = listener;

            await Task.Yield(); // Ensures that the code below is called outside the server mutex lock.

            try
            {
                while (true)
                {
                    (IMultiplexedConnection multiplexedConnection, _) = await listener.AcceptAsync(
                        shutdownCancellationToken).ConfigureAwait(false);
                    _ = Task.Run(async () =>
                    {
                        TransportConnectionInformation transportConnectionInformation;
                        try
                        {
                            transportConnectionInformation = await multiplexedConnection.ConnectAsync(
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            await multiplexedConnection.DisposeAsync().ConfigureAwait(false);
                            return;
                        }

                        IProtocolConnection? protocolConnection = null;
                        lock (_mutex)
                        {
                            // If server is not shutdown and the max connection count is not reached, create the
                            // protocol connection.
                            if (!shutdownCancellationToken.IsCancellationRequested &&
                                (_options.MaxConnections == 0 || _connections.Count < _options.MaxConnections))
                            {
                                protocolConnection = new IceRpcProtocolConnection(
                                    multiplexedConnection,
                                    transportConnectionInformation,
                                    _options.ConnectionOptions);

                                protocolConnection = new MetricsProtocolConnectionDecorator(protocolConnection);
                                if (_logger is not null)
                                {
                                    protocolConnection = new LogProtocolConnectionDecorator(
                                        protocolConnection,
                                        transportConnectionInformation.RemoteNetworkAddress,
                                        _logger);
                                }

                                _connections.Add(protocolConnection);
                            }
                        }

                        if (shutdownCancellationToken.IsCancellationRequested)
                        {
                            // Server is shutdown, dispose the transport connection.
                            await multiplexedConnection.DisposeAsync().ConfigureAwait(false);
                        }
                        else if (protocolConnection is null)
                        {
                            // The max connection count is reached, refuse the connection.
                            try
                            {
                                await multiplexedConnection.CloseAsync(
                                    (ulong)IceRpcConnectionErrorCode.Refused,
                                    shutdownCancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                // ignore and continue
                            }

                            await multiplexedConnection.DisposeAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            await ConnectConnectionAsync(
                                protocolConnection,
                                shutdownCancellationToken).ConfigureAwait(false);
                        }
                    });
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
                _shutdownCompleteSource.TrySetException(exception);
            }
        }

        async Task ConnectConnectionAsync(IProtocolConnection connection, CancellationToken shutdownCancellationToken)
        {
            // Schedule removal after addition, outside mutex lock.
            _ = RemoveFromCollectionAsync(connection, shutdownCancellationToken);

            // Connect the connection. Don't need to pass a cancellation token here ConnectAsync creates one with the
            // configured connection timeout.
            await connection.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Remove the connection from _connections once shutdown completes
        async Task RemoveFromCollectionAsync(
            IProtocolConnection connection,
            CancellationToken shutdownCancellationToken)
        {
            try
            {
                await connection.ShutdownComplete.WaitAsync(shutdownCancellationToken).ConfigureAwait(false);
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
                    _backgroundConnectionDisposeCount++;
                }
            }

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                lock (_mutex)
                {
                    if (--_backgroundConnectionDisposeCount == 0 && shutdownCancellationToken.IsCancellationRequested)
                    {
                        _backgroundConnectionDisposeTcs.SetResult();
                    }
                }
            }
        }
    }

    /// <summary>Shuts down this server: the server stops accepting new connections and shuts down gracefully all its
    /// existing connections.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
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

                if (_listener is not null)
                {
                    // Stop accepting new connections by disposing of the listener.
                    _listenerDisposeTask = _listener.DisposeAsync().AsTask();
                    _listener = null;
                }
            }

            if (_listenerDisposeTask is not null)
            {
                await _listenerDisposeTask.ConfigureAwait(false);
            }

            await Task.WhenAll(_connections.Select(entry => entry.ShutdownAsync(cancellationToken)))
                .ConfigureAwait(false);

            // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
            // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
            // using Result or Wait()), ShutdownAsync will complete.
            _ = _shutdownCompleteSource.TrySetResult();
        }
        catch (Exception exception)
        {
            _ = _shutdownCompleteSource.TrySetException(exception);
            throw;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => ServerAddress.ToString();

    private class LogDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IDuplexConnection> _decoratee;
        private readonly ILogger _logger;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                    await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

                _logger.ConnectionAccepted(ServerAddress, remoteNetworkAddress);

                return (
                    new LogDuplexConnectionDecorator(connection, remoteNetworkAddress, _logger),
                    remoteNetworkAddress);
            }
            catch (Exception exception)
            {
                _logger.ConnectionAcceptFailed(ServerAddress, exception);
                throw;
            }
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal LogDuplexListenerDecorator(IListener<IDuplexConnection> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logger.StartAcceptingConnections(ServerAddress);
        }
    }

    private class LogMultiplexedListenerDecorator : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IMultiplexedConnection> _decoratee;
        private readonly ILogger _logger;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                (IMultiplexedConnection connection, EndPoint remoteNetworkAddress) =
                    await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

                _logger.ConnectionAccepted(ServerAddress, remoteNetworkAddress);

                return (
                    new LogMultiplexedConnectionDecorator(connection, remoteNetworkAddress, _logger),
                    remoteNetworkAddress);
            }
            catch (Exception exception)
            {
                _logger.ConnectionAcceptFailed(ServerAddress, exception);
                throw;
            }
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal LogMultiplexedListenerDecorator(IListener<IMultiplexedConnection> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logger.StartAcceptingConnections(ServerAddress);
        }
    }

    /// <summary>Provides a decorator that adds logging to the <see cref="IDuplexConnection" />.</summary>
    private class LogDuplexConnectionDecorator : IDuplexConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IDuplexConnection _decoratee;
        private readonly ILogger _logger;
        private readonly EndPoint _remoteNetworkAddress;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(
                    cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                _logger.ConnectionConnectFailed(ServerAddress, _remoteNetworkAddress, exception);
                throw;
            }
        }

        public void Dispose() => _decoratee.Dispose();

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            _decoratee.ReadAsync(buffer, cancellationToken);

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            // TODO: log connection refuse. How?
            _decoratee.ShutdownAsync(cancellationToken);

        public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken) =>
            _decoratee.WriteAsync(buffers, cancellationToken);

        internal LogDuplexConnectionDecorator(
            IDuplexConnection decoratee,
            EndPoint remoteNetworkAddress,
            ILogger logger)
        {
            _decoratee = decoratee;
            _remoteNetworkAddress = remoteNetworkAddress;
            _logger = logger;
        }
    }

    /// <summary>Provides a decorator that adds logging to the <see cref="IMultiplexedConnection" />.</summary>
    private class LogMultiplexedConnectionDecorator : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IMultiplexedConnection _decoratee;
        private readonly ILogger _logger;
        private readonly EndPoint _remoteNetworkAddress;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(
                    cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                _logger.ConnectionConnectFailed(ServerAddress, _remoteNetworkAddress, exception);
                throw;
            }
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _decoratee.AcceptStreamAsync(cancellationToken);

        public Task CloseAsync(ulong applicationErrorCode, CancellationToken cancellationToken) =>
            // TODO: log connection refuse. How?
            _decoratee.CloseAsync(applicationErrorCode, cancellationToken);

        public ValueTask<IMultiplexedStream> CreateStreamAsync(
            bool bidirectional,
            CancellationToken cancellationToken) =>
            _decoratee.CreateStreamAsync(bidirectional, cancellationToken);

        internal LogMultiplexedConnectionDecorator(
            IMultiplexedConnection decoratee,
            EndPoint remoteNetworkAddress,
            ILogger logger)
        {
            _decoratee = decoratee;
            _remoteNetworkAddress = remoteNetworkAddress;
            _logger = logger;
        }
    }

    /// <summary>Provides a decorator that adds logging to the <see cref="IProtocolConnection" />.</summary>
    private class LogProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly IProtocolConnection _decoratee;
        private readonly ILogger _logger;
        private readonly Task _logShutdownTask;
        private EndPoint? _localNetworkAddress;
        private readonly EndPoint _remoteNetworkAddress;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                _localNetworkAddress = result.LocalNetworkAddress;
                _logger.ConnectionConnected(isServer: true, _localNetworkAddress, _remoteNetworkAddress);
                return result;
            }
            catch (Exception exception)
            {
                _logger.ConnectionConnectFailed(ServerAddress, _remoteNetworkAddress, exception);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            await _logShutdownTask.ConfigureAwait(false);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(cancellationToken);

        internal LogProtocolConnectionDecorator(
            IProtocolConnection decoratee,
            EndPoint remoteNetworkAddress,
            ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _remoteNetworkAddress = remoteNetworkAddress;

            _logShutdownTask = LogShutdownAsync();

            // This task executes once per decorated connection.
            async Task LogShutdownAsync()
            {
                try
                {
                    await ShutdownComplete.ConfigureAwait(false);
                    if (_localNetworkAddress is not null)
                    {
                        _logger.ConnectionShutdown(isServer: true, _localNetworkAddress, _remoteNetworkAddress);
                    }
                }
                catch (Exception exception)
                {
                    if (_localNetworkAddress is not null)
                    {
                        _logger.ConnectionFailed(
                            isServer: true,
                            _localNetworkAddress,
                            remoteNetworkAddress,
                            exception);
                    }
                }
            }
        }
    }

    /// <summary>Provides a decorator that adds metrics to a <see cref="IListener{IDuplexConnection}" />.</summary>
    private class MetricsDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IDuplexConnection> _decoratee;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            ServerMetrics.Instance.ConnectionStart();
            return (new MetricsDuplexConnectionDecorator(connection), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal MetricsDuplexListenerDecorator(IListener<IDuplexConnection> decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds metrics to a <see cref="IListener{IMultiplexedConnection}" />.</summary>
    private class MetricsMultiplexedListenerDecorator : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IMultiplexedConnection> _decoratee;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            ServerMetrics.Instance.ConnectionStart();
            return (new MetricsMultiplexedConnectionDecorator(connection), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal MetricsMultiplexedListenerDecorator(IListener<IMultiplexedConnection> decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds logging to the <see cref="IDuplexConnection" />.</summary>
    private class MetricsDuplexConnectionDecorator : IDuplexConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IDuplexConnection _decoratee;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ServerMetrics.Instance.ConnectStart();
            try
            {
                return await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ServerMetrics.Instance.ConnectStop();
                throw;
            }
        }

        public void Dispose() => _decoratee.Dispose();

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            _decoratee.ReadAsync(buffer, cancellationToken);

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            _decoratee.ShutdownAsync(cancellationToken);

        public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken) =>
            _decoratee.WriteAsync(buffers, cancellationToken);

        internal MetricsDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds logging to the <see cref="IMultiplexedConnection" />.</summary>
    private class MetricsMultiplexedConnectionDecorator : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IMultiplexedConnection _decoratee;

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _decoratee.AcceptStreamAsync(cancellationToken);

        public Task CloseAsync(ulong applicationErrorCode, CancellationToken cancellationToken) =>
            _decoratee.CloseAsync(applicationErrorCode, cancellationToken);

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ServerMetrics.Instance.ConnectStart();
            try
            {
                return await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ServerMetrics.Instance.ConnectStop();
                throw;
            }
        }

        public ValueTask<IMultiplexedStream> CreateStreamAsync(
            bool bidirectional,
            CancellationToken cancellationToken) => _decoratee.CreateStreamAsync(bidirectional, cancellationToken);

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal MetricsMultiplexedConnectionDecorator(IMultiplexedConnection decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds metrics to the <see cref="IProtocolConnection" />.
    /// </summary>
    private class MetricsProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly IProtocolConnection _decoratee;
        private readonly Task _shutdownTask;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                ServerMetrics.Instance.ConnectSuccess();
                return result;
            }
            finally
            {
                ServerMetrics.Instance.ConnectStop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            await _shutdownTask.ConfigureAwait(false);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(cancellationToken);

        internal MetricsProtocolConnectionDecorator(IProtocolConnection decoratee)
        {
            _decoratee = decoratee;

            _shutdownTask = ShutdownAsync();

            // This task executes once per decorated connection.
            async Task ShutdownAsync()
            {
                try
                {
                    await ShutdownComplete.ConfigureAwait(false);
                }
                catch
                {
                    ServerMetrics.Instance.ConnectionFailure();
                }
                ServerMetrics.Instance.ConnectionStop();
            }
        }
    }
}
