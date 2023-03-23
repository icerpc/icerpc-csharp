// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc;

/// <summary>A server accepts connections from clients and dispatches the requests it receives over these connections.
/// </summary>
public sealed class Server : IAsyncDisposable
{
    private readonly LinkedList<IProtocolConnection> _connections = new();

    private readonly TimeSpan _connectTimeout;

    // A detached connection is a protocol connection that we've decided to connect, or that is connecting, shutting
    // down or being disposed. It counts towards _maxConnections and both Server.ShutdownAsync and DisposeAsync wait for
    // detached connections to reach 0 using _detachedConnectionsTcs. Such a connection is "detached" because it's not
    // in _connections.
    private int _detachedConnectionCount;

    private readonly TaskCompletionSource _detachedConnectionsTcs = new();

    // A cancellation token source that is canceled by DisposeAsync.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;

    private readonly Func<IConnectorListener> _listenerFactory;

    private Task? _listenTask;

    private readonly int _maxConnections;

    private readonly int _maxPendingConnections;

    private readonly object _mutex = new();

    private readonly ServerAddress _serverAddress;

    // A cancellation token source canceled by ShutdownAsync and DisposeAsync.
    private readonly CancellationTokenSource _shutdownCts;

    private Task? _shutdownTask;

    private readonly TimeSpan _shutdownTimeout;

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. The <see
    /// langword="null" /> value is equivalent to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. The <see
    /// langword="null" /> value is equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The logger. <see langword="null" /> is equivalent to <see cref="NullLogger.Instance"
    /// />.</param>
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

        logger ??= NullLogger.Instance;

        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);

        duplexServerTransport ??= IDuplexServerTransport.Default;
        multiplexedServerTransport ??= IMultiplexedServerTransport.Default;
        _maxConnections = options.MaxConnections;
        _maxPendingConnections = options.MaxPendingConnections;

        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;

        _serverAddress = options.ServerAddress;
        if (_serverAddress.Transport is null)
        {
            _serverAddress = _serverAddress with
            {
                Transport = _serverAddress.Protocol == Protocol.Ice ?
                    duplexServerTransport.Name : multiplexedServerTransport.Name
            };
        }

        _listenerFactory = () =>
        {
            IConnectorListener listener;
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

                listener = new IceConnectorListener(
                    transportListener,
                    options.ConnectionOptions);
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
                        Pool = options.ConnectionOptions.Pool
                    },
                    options.ServerAuthenticationOptions);

                listener = new IceRpcConnectorListener(
                    transportListener,
                    options.ConnectionOptions,
                    logger == NullLogger.Instance ? null : new LogTaskExceptionObserver(logger));
            }

            listener = new MetricsConnectorListenerDecorator(listener);
            if (logger != NullLogger.Instance)
            {
                listener = new LogConnectorListenerDecorator(listener, logger);
            }
            return listener;
        };
    }

    /// <summary>Constructs a server with the specified dispatcher and authentication options. All other properties
    /// use the <see cref="ServerOptions" /> defaults.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options. When not <see langword="null"
    /// />, the server will accept only secure connections.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. <see langword="null"
    /// /> is equivalent to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. <see
    /// langword="null" /> is equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The logger. <see langword="null" /> is equivalent to <see cref="NullLogger.Instance"
    /// />.</param>
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

    /// <summary>Constructs a server with the specified dispatcher, server address and authentication options. All
    /// other properties use the <see cref="ServerOptions" /> defaults.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddress">The server address of the server.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options. When not <see langword="null"
    /// />, the server will accept only secure connections.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. <see langword="null"
    /// /> is equivalent to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. <see
    /// langword="null" /> is equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The logger. <see langword="null" /> is equivalent to <see cref="NullLogger.Instance"
    /// />.</param>
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
    /// other properties use the <see cref="ServerOptions" /> defaults.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddressUri">A URI that represents the server address of the server.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options. When not <see langword="null"
    /// />, the server will accept only secure connections.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. <see langword="null"
    /// /> is equivalent to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. <see
    /// langword="null" /> is equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    /// <param name="logger">The logger. <see langword="null" /> is equivalent to <see cref="NullLogger.Instance"
    /// />.</param>
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

    /// <summary>Releases all resources allocated by this server. The server stops listening for new connections and
    /// disposes the connections it accepted from clients.</summary>
    /// <returns>A value task that completes when the disposal of all connections accepted by the server has completed.
    /// This includes connections that were active when this method is called and connections whose disposal was
    /// initiated prior to this call.</returns>
    /// <remarks>The disposal of a connection waits for the completion of all dispatch tasks created by this connection.
    /// If the configured dispatcher does not complete promptly when its cancellation token is canceled, the disposal of
    /// a connection and indirectly of the server as a whole can hang.</remarks>
    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_disposeTask is null)
            {
                _shutdownTask ??= Task.CompletedTask;
                if (_detachedConnectionCount == 0)
                {
                    _ = _detachedConnectionsTcs.TrySetResult();
                }

                _disposeTask = PerformDisposeAsync();
            }
            return new(_disposeTask);
        }

        async Task PerformDisposeAsync()
        {
            await Task.Yield(); // exit mutex lock

            _disposedCts.Cancel();

            // _listenTask etc are immutable when _disposeTask is not null.

            if (_listenTask is not null)
            {
                // Wait for shutdown before disposing connections.
                try
                {
                    await Task.WhenAll(_listenTask, _shutdownTask).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore exceptions.
                }

                await Task.WhenAll(
                    _connections
                        .Select(connection => connection.DisposeAsync().AsTask())
                        .Append(_detachedConnectionsTcs.Task)).ConfigureAwait(false);
            }

            _disposedCts.Dispose();
            _shutdownCts.Dispose();
        }
    }

    /// <summary>Starts accepting connections on the configured server address. Requests received over these connections
    /// are then dispatched by the configured dispatcher.</summary>
    /// <returns>The server address this server is listening on and that a client would connect to. This address is the
    /// same as <see cref="ServerOptions.ServerAddress" /> except its <see cref="ServerAddress.Transport" /> property is
    /// always non-null and its port number is never 0 when the host is an IP address.</returns>
    /// <exception cref="IceRpcException">Thrown when the server transport fails to listen on the configured <see
    /// cref="ServerOptions.ServerAddress" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or shutting
    /// down.</exception>
    /// <exception cref="ObjectDisposedException">Throw when the server is disposed.</exception>
    public ServerAddress Listen()
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException($"Server '{this}' is shut down or shutting down.");
            }
            if (_listenTask is not null)
            {
                throw new InvalidOperationException($"Server '{this}' is already listening.");
            }

            IConnectorListener listener = _listenerFactory();
            _listenTask = ListenAsync(listener); // _listenTask owns listener and must dispose it
            return listener.ServerAddress;
        }

        async Task ListenAsync(IConnectorListener listener)
        {
            await Task.Yield(); // exit mutex lock

            try
            {
                using var pendingConnectionSemaphore = new SemaphoreSlim(
                    _maxPendingConnections,
                    _maxPendingConnections);

                while (!_shutdownCts.IsCancellationRequested)
                {
                    await pendingConnectionSemaphore.WaitAsync(_shutdownCts.Token).ConfigureAwait(false);

                    IConnector? connector = null;
                    do
                    {
                        try
                        {
                            (connector, _) = await listener.AcceptAsync(_shutdownCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (IsRetryableAcceptException(exception))
                        {
                            // continue
                        }
                    }
                    while (connector is null);

                    // We don't wait for the connection to be activated or shutdown. This could take a while for some
                    // transports such as TLS based transports where the handshake requires few round trips between the
                    // client and server. Waiting could also cause a security issue if the client doesn't respond to the
                    // connection initialization as we wouldn't be able to accept new connections in the meantime. The
                    // call will eventually timeout if the ConnectTimeout expires.
                    CancellationToken cancellationToken = _disposedCts.Token;
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await ConnectAsync(connector, cancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Ignore connection establishment failure. This failures are logged by the
                                // LogConnectorDecorator
                            }
                            finally
                            {
                                // The connection dispose will dispose the transport connection if it has not been
                                // adopted by the protocol connection.
                                await connector.DisposeAsync().ConfigureAwait(false);

                                // The pending connection semaphore is disposed by the listen task completion once
                                // shutdown / dispose is initiated.
                                lock (_mutex)
                                {
                                    if (_shutdownTask is null)
                                    {
                                        pendingConnectionSemaphore.Release();
                                    }
                                }
                            }
                        },
                        CancellationToken.None); // the task must run to dispose the connector.
                }
            }
            catch
            {
                // Ignore. Exceptions thrown by listener.AcceptAsync are logged by the log decorator when appropriate.
            }
            finally
            {
                await listener.DisposeAsync().ConfigureAwait(false);
            }

            async Task ConnectAsync(IConnector connector, CancellationToken cancellationToken)
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(_connectTimeout);

                // Connect the transport connection first. This connection establishment can be interrupted by the
                // connect timeout or the server ShutdownAsync/DisposeAsync.
                TransportConnectionInformation transportConnectionInformation =
                    await connector.ConnectTransportConnectionAsync(connectCts.Token).ConfigureAwait(false);

                IProtocolConnection? protocolConnection = null;
                bool serverBusy = false;

                lock (_mutex)
                {
                    Debug.Assert(
                        _maxConnections == 0 || _connections.Count + _detachedConnectionCount <= _maxConnections);

                    if (_shutdownTask is null)
                    {
                        if (_maxConnections > 0 && (_connections.Count + _detachedConnectionCount) == _maxConnections)
                        {
                            serverBusy = true;
                        }
                        else
                        {
                            // The protocol connection adopts the transport connection from the connector and it's
                            // now responsible for disposing of it.
                            protocolConnection = connector.CreateProtocolConnection(transportConnectionInformation);
                            _detachedConnectionCount++;
                        }
                    }
                }

                if (protocolConnection is null)
                {
                    try
                    {
                        await connector.RefuseTransportConnectionAsync(serverBusy, connectCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore and continue
                    }
                    // The transport connection is disposed by the disposal of the connector.
                }
                else
                {
                    Task shutdownRequested;
                    try
                    {
                        (_, shutdownRequested) = await protocolConnection.ConnectAsync(connectCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        await DisposeDetachedConnectionAsync(protocolConnection, withShutdown: false)
                            .ConfigureAwait(false);
                        throw;
                    }

                    LinkedListNode<IProtocolConnection>? listNode = null;

                    lock (_mutex)
                    {
                        if (_shutdownTask is null)
                        {
                            listNode = _connections.AddLast(protocolConnection);

                            // protocolConnection is no longer a detached connection since it's now "attached" in
                            // _connections.
                            _detachedConnectionCount--;
                        }
                    }

                    if (listNode is null)
                    {
                        await DisposeDetachedConnectionAsync(protocolConnection, withShutdown: true)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // Schedule removal after successful ConnectAsync.
                        _ = ShutdownWhenRequestedAsync(protocolConnection, shutdownRequested, listNode);
                    }
                }
            }
        }

        async Task DisposeDetachedConnectionAsync(IProtocolConnection connection, bool withShutdown)
        {
            if (withShutdown)
            {
                // _disposedCts is not disposed since we own a _backgroundConnectionDisposeCount.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
                cts.CancelAfter(_shutdownTimeout);

                try
                {
                    // Can be canceled by DisposeAsync or the shutdown timeout.
                    await connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore connection shutdown failures. connection.ShutdownAsync makes sure it's an "expected"
                    // exception.
                }
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            lock (_mutex)
            {
                if (--_detachedConnectionCount == 0 && _shutdownTask is not null)
                {
                    _detachedConnectionsTcs.SetResult();
                }
            }
        }

        // Remove the connection from _connections after a successful ConnectAsync.
        async Task ShutdownWhenRequestedAsync(
            IProtocolConnection connection,
            Task shutdownRequested,
            LinkedListNode<IProtocolConnection> listNode)
        {
            await shutdownRequested.ConfigureAwait(false);

            lock (_mutex)
            {
                if (_shutdownTask is null)
                {
                    _connections.Remove(listNode);
                    _detachedConnectionCount++;
                }
                else
                {
                    // _connections is immutable and ShutdownAsync/DisposeAsync is responsible to shutdown/dispose
                    // this connection.
                    return;
                }
            }

            await DisposeDetachedConnectionAsync(connection, withShutdown: true).ConfigureAwait(false);
        }
    }

    /// <summary>Gracefully shuts down this server: the server stops accepting new connections and shuts down gracefully
    /// all its connections.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes successfully once the shutdown of all connections accepted by the server has
    /// completed. This includes connections that were active when this method is called and connections whose shutdown
    /// was initiated prior to this call. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" /> with error <see cref="IceRpcError.OperationAborted" /> if the
    /// server is disposed while being shut down.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if the shutdown timed out.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the server is disposed.</exception>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException($"Server '{this}' is shut down or shutting down.");
            }

            if (_detachedConnectionCount == 0)
            {
                _detachedConnectionsTcs.SetResult();
            }

            _shutdownTask = PerformShutdownAsync();
        }
        return _shutdownTask;

        async Task PerformShutdownAsync()
        {
            await Task.Yield(); // exit mutex lock

            _shutdownCts.Cancel();

            // _listenTask is immutable once _shutdownTask is not null.
            if (_listenTask is not null)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _disposedCts.Token);

                    cts.CancelAfter(_shutdownTimeout);

                    try
                    {
                        await Task.WhenAll(
                            _connections
                                .Select(connection => connection.ShutdownAsync(cts.Token))
                                .Append(_listenTask.WaitAsync(cts.Token))
                                .Append(_detachedConnectionsTcs.Task.WaitAsync(cts.Token)))
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Ignore _listenTask and connection shutdown exceptions

                        // Throw OperationCanceledException if this WhenAll exception is hiding an OCE.
                        cts.Token.ThrowIfCancellationRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_disposedCts.IsCancellationRequested)
                    {
                        throw new IceRpcException(
                            IceRpcError.OperationAborted,
                            "The shutdown was aborted because the server was disposed.");
                    }
                    else
                    {
                        throw new TimeoutException(
                            $"The server shut down timed out after {_shutdownTimeout.TotalSeconds} s.");
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString() => _serverAddress.ToString();

    /// <summary>Returns true if the <see cref="IConnectorListener.AcceptAsync" /> failure can be retried.</summary>
    private static bool IsRetryableAcceptException(Exception exception) =>
        // The AcceptAsync call can fail with OperationAborted during shutdown if it is accepting a connection while the
        // listener is disposed.
        (exception is IceRpcException rpcException && rpcException.IceRpcError != IceRpcError.OperationAborted) ||
        // Transports such as Quic do the SSL handshake when the connection is accepted, this can throw
        // AuthenticationException if it fails.
        exception is AuthenticationException;

    /// <summary>Provides a decorator that adds logging to a <see cref="IConnectorListener" />.</summary>
    private class LogConnectorListenerDecorator : IConnectorListener
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IConnectorListener _decoratee;
        private readonly ILogger _logger;

        public async Task<(IConnector, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                (IConnector connector, EndPoint remoteNetworkAddress) =
                    await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogConnectionAccepted(ServerAddress, remoteNetworkAddress);
                return (
                    new LogConnectorDecorator(connector, ServerAddress, remoteNetworkAddress, _logger),
                    remoteNetworkAddress);
            }
            catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
            {
                // Do not log this exception. The AcceptAsync call can fail with OperationAborted during shutdown if it
                // is accepting a connection while the listener is disposed.
                throw;
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
                // Do not log this exception. The AcceptAsync call can fail with OperationCanceledException during
                // shutdown once the shutdown cancellation token is canceled.
                throw;
            }
            catch (ObjectDisposedException)
            {
                // Do not log this exception. The AcceptAsync call can fail with ObjectDisposedException during
                // shutdown once the listener is disposed.
                throw;
            }
            catch (Exception exception) when (IsRetryableAcceptException(exception))
            {
                _logger.LogConnectionAcceptFailedWithRetryableException(ServerAddress, exception);
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogConnectionAcceptFailed(ServerAddress, exception);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            _logger.LogStopAcceptingConnections(ServerAddress);
            return _decoratee.DisposeAsync();
        }

        internal LogConnectorListenerDecorator(IConnectorListener decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logger.LogStartAcceptingConnections(ServerAddress);
        }
    }

    private class LogConnectorDecorator : IConnector
    {
        private readonly IConnector _decoratee;
        private readonly ILogger _logger;
        private readonly EndPoint _remoteNetworkAddress;
        private readonly ServerAddress _serverAddress;

        public async Task<TransportConnectionInformation> ConnectTransportConnectionAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await _decoratee.ConnectTransportConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogConnectionConnectFailed(_serverAddress, _remoteNetworkAddress, exception);
                throw;
            }
        }

        public IProtocolConnection CreateProtocolConnection(
            TransportConnectionInformation transportConnectionInformation) =>
            new LogProtocolConnectionDecorator(
                _decoratee.CreateProtocolConnection(transportConnectionInformation),
                _serverAddress,
                _remoteNetworkAddress,
                _logger);

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public Task RefuseTransportConnectionAsync(bool serverBusy, CancellationToken cancel) =>
            _decoratee.RefuseTransportConnectionAsync(serverBusy, cancel);

        internal LogConnectorDecorator(
            IConnector decoratee,
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

    /// <summary>Provides a decorator that adds metrics to a <see cref="IConnectorListener" />.</summary>
    private class MetricsConnectorListenerDecorator : IConnectorListener
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IConnectorListener _decoratee;

        public async Task<(IConnector, EndPoint)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IConnector connector, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return (new MetricsConnectorDecorator(connector), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal MetricsConnectorListenerDecorator(IConnectorListener decoratee) =>
            _decoratee = decoratee;
    }

    private class MetricsConnectorDecorator : IConnector
    {
        private readonly IConnector _decoratee;

        public async Task<TransportConnectionInformation> ConnectTransportConnectionAsync(
            CancellationToken cancellationToken)
        {
            Metrics.ServerMetrics.ConnectStart();
            try
            {
                return await _decoratee.ConnectTransportConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Metrics.ServerMetrics.ConnectStop();
                Metrics.ServerMetrics.ConnectionFailure();
                throw;
            }
        }

        public IProtocolConnection CreateProtocolConnection(
            TransportConnectionInformation transportConnectionInformation) =>
                new MetricsProtocolConnectionDecorator(
                    _decoratee.CreateProtocolConnection(transportConnectionInformation),
                    Metrics.ServerMetrics,
                    connectStarted: true);

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public async Task RefuseTransportConnectionAsync(bool serverBusy, CancellationToken cancel)
        {
            try
            {
                await _decoratee.RefuseTransportConnectionAsync(serverBusy, cancel).ConfigureAwait(false);
            }
            finally
            {
                Metrics.ServerMetrics.ConnectionFailure();
                Metrics.ServerMetrics.ConnectStop();
            }
        }

        internal MetricsConnectorDecorator(IConnector decoratee) => _decoratee = decoratee;
    }

    /// <summary>A connector listener accepts a transport connection and returns a <see cref="IConnector" />. The
    /// connector is used to refuse the transport connection or obtain a protocol connection once the transport
    /// connection is connected.</summary>
    private interface IConnectorListener : IAsyncDisposable
    {
        ServerAddress ServerAddress { get; }

        Task<(IConnector, EndPoint)> AcceptAsync(CancellationToken cancel);
    }

    /// <summary>A connector is returned by <see cref="IConnectorListener" />. The connector allows to connect the
    /// transport connection. If successful, the transport connection can either be refused or accepted by creating the
    /// protocol connection out of it.</summary>
    private interface IConnector : IAsyncDisposable
    {
        Task<TransportConnectionInformation> ConnectTransportConnectionAsync(CancellationToken cancellationToken);

        IProtocolConnection CreateProtocolConnection(TransportConnectionInformation transportConnectionInformation);

        Task RefuseTransportConnectionAsync(bool serverBusy, CancellationToken cancel);
    }

    private class IceConnectorListener : IConnectorListener
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        private readonly IListener<IDuplexConnection> _listener;
        private readonly ConnectionOptions _options;

        public ValueTask DisposeAsync() => _listener.DisposeAsync();

        public async Task<(IConnector, EndPoint)> AcceptAsync(CancellationToken cancel)
        {
            (IDuplexConnection transportConnection, EndPoint remoteNetworkAddress) = await _listener.AcceptAsync(
                cancel).ConfigureAwait(false);
            return (new IceConnector(transportConnection, _options), remoteNetworkAddress);
        }

        internal IceConnectorListener(IListener<IDuplexConnection> listener, ConnectionOptions options)
        {
            _listener = listener;
            _options = options;
        }
    }

    private class IceConnector : IConnector
    {
        private readonly ConnectionOptions _options;
        private IDuplexConnection? _transportConnection;

        public Task<TransportConnectionInformation> ConnectTransportConnectionAsync(
            CancellationToken cancellationToken) =>
            _transportConnection!.ConnectAsync(cancellationToken);

        public IProtocolConnection CreateProtocolConnection(
            TransportConnectionInformation transportConnectionInformation)
        {
            // The protocol connection takes ownership of the transport connection.
            var protocolConnection = new IceProtocolConnection(
                _transportConnection!,
                transportConnectionInformation,
                _options);
            _transportConnection = null;
            return protocolConnection;
        }

        public ValueTask DisposeAsync()
        {
            _transportConnection?.Dispose();
            return new();
        }

        public Task RefuseTransportConnectionAsync(bool serverBusy, CancellationToken cancellationToken)
        {
            _transportConnection!.Dispose();
            return Task.CompletedTask;
        }

        internal IceConnector(IDuplexConnection transportConnection, ConnectionOptions options)
        {
            _transportConnection = transportConnection;
            _options = options;
        }
    }

    private class IceRpcConnectorListener : IConnectorListener
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        private readonly IListener<IMultiplexedConnection> _listener;
        private readonly ConnectionOptions _options;
        private readonly ITaskExceptionObserver? _taskExceptionObserver;

        public async Task<(IConnector, EndPoint)> AcceptAsync(CancellationToken cancel)
        {
            (IMultiplexedConnection transportConnection, EndPoint remoteNetworkAddress) = await _listener.AcceptAsync(
                cancel).ConfigureAwait(false);
            return (new IceRpcConnector(transportConnection, _options, _taskExceptionObserver), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();

        internal IceRpcConnectorListener(
            IListener<IMultiplexedConnection> listener,
            ConnectionOptions options,
            ITaskExceptionObserver? taskExceptionObserver)
        {
            _listener = listener;
            _options = options;
            _taskExceptionObserver = taskExceptionObserver;
        }
    }

    private class IceRpcConnector : IConnector
    {
        private readonly ConnectionOptions _options;
        private readonly ITaskExceptionObserver? _taskExceptionObserver;
        private IMultiplexedConnection? _transportConnection;

        public Task<TransportConnectionInformation> ConnectTransportConnectionAsync(
            CancellationToken cancellationToken) =>
            _transportConnection!.ConnectAsync(cancellationToken);

        public IProtocolConnection CreateProtocolConnection(
            TransportConnectionInformation transportConnectionInformation)
        {
            // The protocol connection takes ownership of the transport connection.
            var protocolConnection = new IceRpcProtocolConnection(
                _transportConnection!,
                transportConnectionInformation,
                _options,
                _taskExceptionObserver);
            _transportConnection = null;
            return protocolConnection;
        }

        public ValueTask DisposeAsync() => _transportConnection?.DisposeAsync() ?? new();

        public Task RefuseTransportConnectionAsync(bool serverBusy, CancellationToken cancellationToken) =>
            _transportConnection!.CloseAsync(
                serverBusy ? MultiplexedConnectionCloseError.ServerBusy : MultiplexedConnectionCloseError.Refused,
                cancellationToken);

        internal IceRpcConnector(
            IMultiplexedConnection transportConnection,
            ConnectionOptions options,
            ITaskExceptionObserver? taskExceptionObserver)
        {
            _transportConnection = transportConnection;
            _options = options;
            _taskExceptionObserver = taskExceptionObserver;
        }
    }
}
