// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
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

    private IConnectionListener? _listener;

    // We keep this task to await the completion of listener's DisposeAsync when making concurrent calls to
    // ShutdownAsync and/or DisposeAsync.
    private Task? _listenerDisposeTask;

    private readonly Func<IConnectionListener> _listenerFactory;

    private Task? _listenTask;

    private readonly int _maxConnections;

    // protects _listener and _connections
    private readonly object _mutex = new();

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

        _listenerFactory = () =>
        {
            // This is the composition root for the protocol and transport listeners.
            IConnectionListener listener;
            if (_serverAddress.Protocol == Protocol.Ice)
            {
                IListener<IDuplexConnection> transportListener = duplexServerTransport.Listen(
                    _serverAddress,
                    new DuplexConnectionOptions
                    {
                        MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                        Pool = options.ConnectionOptions.Pool,
                    },
                    options.ServerAuthenticationOptions);
                listener = new IceConnectionListener(transportListener, options.ConnectionOptions);
            }
            else
            {
                IListener<IMultiplexedConnection> transportListener = multiplexedServerTransport.Listen(
                    _serverAddress,
                    new MultiplexedConnectionOptions
                    {
                        MaxBidirectionalStreams = options.ConnectionOptions.MaxIceRpcBidirectionalStreams,
                        // Add an additional stream for the icerpc protocol control stream.
                        MaxUnidirectionalStreams = options.ConnectionOptions.MaxIceRpcUnidirectionalStreams + 1,
                        MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                        Pool = options.ConnectionOptions.Pool,
                        PayloadErrorCodeConverter = IceRpcProtocol.Instance.PayloadErrorCodeConverter
                    },
                    options.ServerAuthenticationOptions);
                listener = new IceRpcConnectionListener(transportListener, options.ConnectionOptions);
            }

            listener = new MetricsListenerDecorator(listener);
            if (logger is not null)
            {
                listener = new LogListenerDecorator(listener, logger);
            }
            return listener;
        };
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
        IConnectionListener listener;

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

            listener = _listenerFactory();
            _listener = listener;
        }

        _listenTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    (IConnectionConnector connector, _) = await listener.AcceptAsync(shutdownCancellationToken)
                        .ConfigureAwait(false);

                    // We don't wait for the connection to be activated or shutdown. This could take a while for some
                    // transports such as TLS based transports where the handshake requires few round trips between the
                    // client and server. Waiting could also cause a security issue if the client doesn't respond to the
                    // connection initialization as we wouldn't be able to accept new connections in the meantime. The
                    // call will eventually timeout if the ConnectTimeout expires.
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await ConnectAsync(connector).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Ignore connection establishment failure.
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
        });

        async Task ConnectAsync(IConnectionConnector connector)
        {
            IProtocolConnection protocolConnection = await connector.ConnectTransportConnectionAsync(
                shutdownCancellationToken).ConfigureAwait(false);

            Func<Task> executeFunc;
            lock (_mutex)
            {
                // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // TODO: Why don't we use BackgroundConnectionDisposeAsync here?
                    executeFunc = () => protocolConnection.DisposeAsync().AsTask();
                }
                else if (_maxConnections > 0 && _connections.Count == _maxConnections)
                {
                    // We have too many connections and can't accept any more. Reject the underlying transport
                    // connection.
                    _backgroundConnectionDisposeCount++;
                    executeFunc = async () =>
                        {
                            try
                            {
                                await connector.RefuseTransportConnectionAsync(
                                    shutdownCancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                // ignore and continue
                            }
                            _ = BackgroundConnectionDisposeAsync(protocolConnection, shutdownCancellationToken);
                        };
                }
                else
                {
                    _connections.Add(protocolConnection);
                    executeFunc = async () =>
                        {
                            // Schedule removal after addition, outside mutex lock.
                            _ = RemoveFromCollectionAsync(protocolConnection, shutdownCancellationToken);

                            // Connect the connection. Don't need to pass a cancellation token here ConnectAsync creates
                            // one with the configured connection timeout.
                            await protocolConnection.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                        };
                }
            }

            await executeFunc().ConfigureAwait(false);
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

            _ = BackgroundConnectionDisposeAsync(connection, shutdownCancellationToken);
        }

        async Task BackgroundConnectionDisposeAsync(
            IAsyncDisposable connection,
            CancellationToken shutdownCancellationToken)
        {
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

    /// <summary>Provides a decorator that adds logging to a <see cref="IListener{T}" /> of
    /// <see cref="IProtocolConnection" />.</summary>
    private class LogListenerDecorator : IConnectionListener
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IConnectionListener _decoratee;
        private readonly ILogger _logger;

        public async Task<(IConnectionConnector, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                (IConnectionConnector connector, EndPoint remoteNetworkAddress) =
                    await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
                _logger.ConnectionAccepted(ServerAddress, remoteNetworkAddress);
                return (
                    new LogConnectionConnectorDecorator(connector, ServerAddress, remoteNetworkAddress, _logger),
                    remoteNetworkAddress);
            }
            catch (Exception exception)
            {
                _logger.ConnectionAcceptFailed(ServerAddress, exception);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            _logger.StopAcceptingConnections(ServerAddress);
            return _decoratee.DisposeAsync();
        }

        internal LogListenerDecorator(IConnectionListener decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logger.StartAcceptingConnections(ServerAddress);
        }
    }

    private class LogConnectionConnectorDecorator : IConnectionConnector
    {
        private readonly IConnectionConnector _decoratee;
        private readonly ILogger _logger;
        private readonly EndPoint _remoteNetworkAddress;
        private readonly ServerAddress _serverAddress;

        public async Task<IProtocolConnection> ConnectTransportConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                IProtocolConnection protocolConnection = await _decoratee.ConnectTransportConnectionAsync(
                    cancellationToken).ConfigureAwait(false);
                return new LogProtocolConnectionDecorator(protocolConnection, _remoteNetworkAddress, _logger);
            }
            catch (Exception exception)
            {
                _logger.ConnectionConnectFailed(_serverAddress, _remoteNetworkAddress, exception);
                throw;
            }
        }

        public Task RefuseTransportConnectionAsync(CancellationToken cancel) =>
            _decoratee.RefuseTransportConnectionAsync(cancel);

        internal LogConnectionConnectorDecorator(
            IConnectionConnector decoratee,
            ServerAddress serverAddress,
            EndPoint remoteNetworkAddress,
            ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _serverAddress = serverAddress;
            _remoteNetworkAddress = remoteNetworkAddress;
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
                        _logger.ConnectionShutdown(isServer: true, _localNetworkAddress, remoteNetworkAddress);
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

    /// <summary>Provides a decorator that adds metrics to a <see cref="IListener{T}" /> of
    /// <see cref="IProtocolConnection" />.</summary>
    private class MetricsListenerDecorator : IConnectionListener
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IConnectionListener _decoratee;

        public async Task<(IConnectionConnector, EndPoint)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IConnectionConnector connector, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            ServerMetrics.Instance.ConnectionStart();
            return (new MetricsConnectorDecorator(connector), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal MetricsListenerDecorator(IConnectionListener decoratee) => _decoratee = decoratee;
    }

    private class MetricsConnectorDecorator : IConnectionConnector
    {
        private readonly IConnectionConnector _decoratee;

        public async Task<IProtocolConnection> ConnectTransportConnectionAsync(CancellationToken cancellationToken)
        {
            ServerMetrics.Instance.ConnectStart();
            try
            {
                return new MetricsProtocolConnectionDecorator(await _decoratee.ConnectTransportConnectionAsync(
                    cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                ServerMetrics.Instance.ConnectStop();
                throw;
            }
        }

        public async Task RefuseTransportConnectionAsync(CancellationToken cancel)
        {
            try
            {
                await _decoratee.RefuseTransportConnectionAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                ServerMetrics.Instance.ConnectStop();
            }
        }

        internal MetricsConnectorDecorator(IConnectionConnector decoratee) => _decoratee = decoratee;
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
            // The connector called ConnectStart()

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

    private interface IConnectionListener : IAsyncDisposable
    {
        ServerAddress ServerAddress { get; }

        Task<(IConnectionConnector, EndPoint)> AcceptAsync(CancellationToken cancel);
    }

    private interface IConnectionConnector
    {
        Task<IProtocolConnection> ConnectTransportConnectionAsync(CancellationToken cancel);

        Task RefuseTransportConnectionAsync(CancellationToken cancel);
    }

    private class IceConnectionListener : IConnectionListener
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        private readonly IListener<IDuplexConnection> _listener;
        private readonly ConnectionOptions _options;

        public ValueTask DisposeAsync() => _listener.DisposeAsync();

        public async Task<(IConnectionConnector, EndPoint)> AcceptAsync(CancellationToken cancel)
        {
            (IDuplexConnection transportConnection, EndPoint remoteNetworkAddress) = await _listener.AcceptAsync(
                cancel).ConfigureAwait(false);
            return (new IceConnectionConnector(transportConnection, _options), remoteNetworkAddress);
        }

        internal IceConnectionListener(IListener<IDuplexConnection> listener, ConnectionOptions options)
        {
            _listener = listener;
            _options = options;
        }
    }

    private class IceConnectionConnector : IConnectionConnector
    {
        private readonly ConnectionOptions _options;
        private readonly IDuplexConnection _transportConnection;

        public async Task<IProtocolConnection> ConnectTransportConnectionAsync(CancellationToken cancellationToken)
        {
            TransportConnectionInformation transportConnectionInformation;
            try
            {
                transportConnectionInformation = await _transportConnection.ConnectAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _transportConnection.Dispose();
                throw;
            }
            // The protocol connection now owns the transport connection.
            return new IceProtocolConnection(_transportConnection, transportConnectionInformation, _options);
        }

        public Task RefuseTransportConnectionAsync(CancellationToken cancel) =>
            _transportConnection.ShutdownAsync(cancel);

        internal IceConnectionConnector(IDuplexConnection transportConnection, ConnectionOptions options)
        {
            _transportConnection = transportConnection;
            _options = options;
        }
    }

    private class IceRpcConnectionListener : IConnectionListener
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        private readonly IListener<IMultiplexedConnection> _listener;
        private readonly ConnectionOptions _options;

        public async Task<(IConnectionConnector, EndPoint)> AcceptAsync(CancellationToken cancel)
        {
            (IMultiplexedConnection transportConnection, EndPoint remoteNetworkAddress) = await _listener.AcceptAsync(
                cancel).ConfigureAwait(false);
            return (new IceRpcConnectionConnector(transportConnection, _options), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();

        internal IceRpcConnectionListener(IListener<IMultiplexedConnection> listener, ConnectionOptions options)
        {
            _listener = listener;
            _options = options;
        }
    }

    private class IceRpcConnectionConnector : IConnectionConnector
    {
        private readonly ConnectionOptions _options;
        private readonly IMultiplexedConnection _transportConnection;

        public async Task<IProtocolConnection> ConnectTransportConnectionAsync(CancellationToken cancellationToken)
        {
            TransportConnectionInformation transportConnectionInformation;
            try
            {
                transportConnectionInformation = await _transportConnection.ConnectAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _transportConnection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            // The protocol connection now owns the transport connection.
            return new IceRpcProtocolConnection(_transportConnection, transportConnectionInformation, _options);
        }

        public ValueTask DisposeAsync() => _transportConnection?.DisposeAsync() ?? new();

        public Task RefuseTransportConnectionAsync(CancellationToken cancel) =>
            _transportConnection.CloseAsync((ulong)IceRpcConnectionErrorCode.Refused, cancel);

        internal IceRpcConnectionConnector(IMultiplexedConnection transportConnection, ConnectionOptions options)
        {
            _transportConnection = transportConnection;
            _options = options;
        }
    }
}
