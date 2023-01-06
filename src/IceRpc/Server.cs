// Copyright (c) ZeroC, Inc. All rights reserved.

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
    private int _backgroundConnectionDisposeCount;

    private readonly TaskCompletionSource _backgroundConnectionDisposeTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _backgroundConnectionShutdownCount;

    private readonly TaskCompletionSource _backgroundConnectionShutdownTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly HashSet<IProtocolConnection> _connections = new();

    private readonly TimeSpan _connectTimeout;

    // A cancellation token source that is canceled when DisposeAsync is called.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;

    private bool _isShutdown;

    // A cancellation token source we cancel when we want to stop listening.
    private readonly CancellationTokenSource _listenCts = new();

    private IConnectorListener? _listener;

    private readonly Func<IConnectorListener> _listenerFactory;

    private Task? _listenTask;

    private readonly int _maxConnections;

    private readonly int _maxPendingConnections;

    // protects _listener and _connections
    private readonly object _mutex = new();

    private readonly ServerAddress _serverAddress;

    private readonly TimeSpan _shutdownTimeout;

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

        duplexServerTransport ??= IDuplexServerTransport.Default;
        multiplexedServerTransport ??= IMultiplexedServerTransport.Default;
        _maxConnections = options.MaxConnections;
        _maxPendingConnections = options.MaxPendingConnections;

        _connectTimeout = options.ConnectionOptions.ConnectTimeout;
        _shutdownTimeout = options.ConnectionOptions.ShutdownTimeout;

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
            // This is the composition root for the protocol and transport listeners.
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
                listener = new IceConnectorListener(transportListener, options.ConnectionOptions);
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
                listener = new IceRpcConnectorListener(transportListener, options.ConnectionOptions);
            }

            listener = new MetricsConnectorListenerDecorator(listener);
            listener = new LogAndRetryConnectorListenerDecorator(listener, logger);
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
                _listenCts.Cancel();
                _disposedCts.Cancel();

                _disposeTask = PerformDisposeAsync();

                if (_backgroundConnectionDisposeCount == 0)
                {
                    // There is no outstanding background dispose.
                    _ = _backgroundConnectionDisposeTcs.TrySetResult();
                }
            }
            return new(_disposeTask);
        }

        async Task PerformDisposeAsync()
        {
            await Task.Yield(); // exit mutex lock

            // _listener, _listenTask etc are immutable when _disposeTask is not null.

            if (_listener is not null)
            {
                await _listener.DisposeAsync().ConfigureAwait(false);
            }

            if (_listenTask is not null)
            {
                try
                {
                    await _listenTask.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Debug.Fail($"Unexpected listen exception: {exception}");
                }
            }

            await Task.WhenAll(_connections.Select(connection => connection.DisposeAsync().AsTask()))
                .ConfigureAwait(false);

            await _backgroundConnectionDisposeTcs.Task.ConfigureAwait(false);

            _listenCts.Dispose();
            _disposedCts.Dispose();
        }
    }

    /// <summary>Starts accepting connections on the configured server address. Requests received over these connections
    /// are then dispatched by the configured dispatcher.</summary>
    /// <returns>The server address this server is listening on and that a client would connect to. This address is the
    /// same as <see cref="ServerOptions.ServerAddress" /> except its <see cref="ServerAddress.Transport" /> property is
    /// always non-null and its port number is never 0 when the host is an IP address.</returns>
    /// <exception cref="IceRpcException">Thrown when another server is already listening on the same server address.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or shutting
    /// down.</exception>
    /// <exception cref="ObjectDisposedException">Throw when the server is disposed.</exception>
    public ServerAddress Listen()
    {
        CancellationToken disposedCancellationToken;
        IConnectorListener listener;

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(Server)}");
            }
            if (_isShutdown)
            {
                throw new InvalidOperationException($"Server '{this}' is shut down or shutting down.");
            }
            if (_listener is not null)
            {
                throw new InvalidOperationException($"Server '{this}' is already listening.");
            }

            listener = _listenerFactory();
            _listener = listener;

            // disposedCancellationToken remains valid even after _disposedCts is disposed.
            disposedCancellationToken = _disposedCts.Token;
            _listenTask = Task.Run(() => ListenAsync(_listenCts.Token));
        }

        return listener.ServerAddress;

        async Task ListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var pendingConnectionSemaphore = new SemaphoreSlim(
                    _maxPendingConnections,
                    _maxPendingConnections);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await pendingConnectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                    (IConnector connector, _) = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);

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
                                    if (_disposeTask is null && !_isShutdown)
                                    {
                                        pendingConnectionSemaphore.Release();
                                    }
                                }
                            }
                        },
                        CancellationToken.None);
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
            catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
            {
                // The AcceptAsync call can fail with OperationAborted during shutdown if it is accepting a connection
                // while the listener is disposed.
            }
            // other exceptions thrown by listener.AcceptAsync are logged by listener via a log decorator

            async Task ConnectAsync(IConnector connector)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(disposedCancellationToken);
                cts.CancelAfter(_connectTimeout);

                // Connect the transport connection first. This connection establishment can be interrupted by the
                // connect timeout or the server disposal.
                TransportConnectionInformation transportConnectionInformation =
                    await connector.ConnectTransportConnectionAsync(cts.Token).ConfigureAwait(false);

                IProtocolConnection? protocolConnection = null;
                bool serverBusy = false;

                // Create the protocol connection if the server is not being disposed and if the max connection
                // count is not reached.
                lock (_mutex)
                {
                    Debug.Assert(_maxConnections == 0 || _connections.Count <= _maxConnections);
                    if (_isShutdown || (_maxConnections > 0 && _connections.Count == _maxConnections))
                    {
                        // TODO: not quite correct for _isShutdown
                        serverBusy = true;
                    }
                    else if (_disposeTask is null)
                    {
                        // The protocol connection adopts the transport connection from the connector and it's now
                        // responsible for disposing of it.
                        protocolConnection = connector.CreateProtocolConnection(transportConnectionInformation);
                        _connections.Add(protocolConnection);
                    }
                }

                if (protocolConnection is not null)
                {
                    // Schedule removal after addition, outside mutex lock.
                    _ = RemoveFromCollectionAsync(protocolConnection);

                    // Connect the connection..
                    _ = await protocolConnection.ConnectAsync(cts.Token).ConfigureAwait(false);
                }
                else if (serverBusy)
                {
                    // If the max connection count is reached, we refuse the transport connection.
                    try
                    {
                        await connector.RefuseTransportConnectionAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore and continue
                    }
                }
                // else the server is shutting down or the connect timeout expired after connecting the transport
                // connection.
                // The transport connection is disposed by the disposal of the connector if the protocol connection
                // didn't adopt it above.
            }

            // Remove the connection from _connections
            async Task RemoveFromCollectionAsync(IProtocolConnection connection)
            {
                bool shutdownRequested =
                    await Task.WhenAny(connection.ShutdownRequested, connection.Closed).ConfigureAwait(false) ==
                        connection.ShutdownRequested;

                lock (_mutex)
                {
                    if (_isShutdown || _disposeTask is not null)
                    {
                        // _connections is immutable and Server.ShutdownAsync/DisposeAsync is responsible to
                        // shutdown/dispose this connection.
                        return;
                    }
                    else
                    {
                        _ = _connections.Remove(connection);
                        _backgroundConnectionDisposeCount++;
                        if (shutdownRequested)
                        {
                            _backgroundConnectionShutdownCount++;
                        }
                    }
                }

                if (shutdownRequested)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(disposedCancellationToken);
                    cts.CancelAfter(_shutdownTimeout);

                    try
                    {
                        await connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IceRpcException)
                    {
                    }
                    catch (Exception exception)
                    {
                        Debug.Fail($"Unexpected connection shutdown exception: {exception}");
                        throw;
                    }
                    finally
                    {
                        lock (_mutex)
                        {
                            if (--_backgroundConnectionShutdownCount == 0 && _isShutdown)
                            {
                                _backgroundConnectionShutdownTcs.SetResult();
                            }
                        }
                    }
                }

                await connection.DisposeAsync().ConfigureAwait(false);

                lock (_mutex)
                {
                    if (--_backgroundConnectionDisposeCount == 0 && _disposeTask is not null)
                    {
                        _backgroundConnectionDisposeTcs.SetResult();
                    }
                }
            }
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
        CancellationToken disposedCancellationToken;

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(Server)}");
            }
            if (_isShutdown)
            {
                throw new InvalidOperationException($"Server '{this}' is shut down or shutting down.");
            }

            _isShutdown = true;
            _listenCts.Cancel();

            if (_backgroundConnectionShutdownCount == 0)
            {
                // There is no outstanding background connection shutdown.
                _ = _backgroundConnectionShutdownTcs.TrySetResult();
            }

            disposedCancellationToken = _disposedCts.Token;
        }

        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                disposedCancellationToken);

            cts.CancelAfter(_shutdownTimeout);

            try
            {
                // _listener and _listenTask immutable once _isShutdown is true.
                if (_listener is not null)
                {
                    // The disposal of the listener will terminate the listenTask
                    await _listener.DisposeAsync().AsTask().WaitAsync(cts.Token).ConfigureAwait(false);
                }

                if (_listenTask is not null)
                {
                    await _listenTask.WaitAsync(cts.Token).ConfigureAwait(false);
                }

                await Task.WhenAll(
                    _connections
                        .Select(entry => entry.ShutdownAsync(cts.Token))
                        .Append(_backgroundConnectionShutdownTcs.Task.WaitAsync(cts.Token)))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (disposedCancellationToken.IsCancellationRequested)
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
            catch
            {
                // Ignore connection shutdown failures
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString() => _serverAddress.ToString();

    /// <summary>Provides a decorator that adds logging to a <see cref="IConnectorListener" /> and retries
    /// accepts failures that represent a problem with the peer connection being accepted.</summary>
    private class LogAndRetryConnectorListenerDecorator : IConnectorListener
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IConnectorListener _decoratee;
        private readonly ILogger _logger;

        public async Task<(IConnector, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    (IConnector connector, EndPoint remoteNetworkAddress) =
                        await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

                    if (_logger == NullLogger.Instance)
                    {
                        return (connector, remoteNetworkAddress);
                    }
                    else
                    {
                        _logger.LogConnectionAccepted(ServerAddress, remoteNetworkAddress);
                        return (
                            new LogConnectorDecorator(connector, ServerAddress, remoteNetworkAddress, _logger),
                            remoteNetworkAddress);
                    }
                }
                catch (IceRpcException exception)
                {
                    if (exception.IceRpcError == IceRpcError.OperationAborted)
                    {
                        // Do not log or continue. The AcceptAsync call can fail with OperationAborted during
                        // shutdown if it is accepting a connection while the listener is disposed.
                        throw;
                    }

                    // IceRpcException with an error code other than OperationAborted indicates a problem with
                    // the connection being accepted. We log the error and try to accept a new connection.
                    _logger.LogConnectionAcceptFailedAndContinue(ServerAddress, exception);
                    // and continue
                }
                catch (AuthenticationException exception)
                {
                    // Transports such as Quic do the SSL handshake when the connection is accepted, this can
                    // throw AuthenticationException if the SSL handshake fails. We log the error and try to
                    // accept a new connection.
                    _logger.LogConnectionAcceptFailedAndContinue(ServerAddress, exception);
                    // and continue
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
                catch (Exception exception)
                {
                    _logger.LogConnectionAcceptFailed(ServerAddress, exception);
                    throw;
                }

                // We want to exit immediately if the cancellation token is canceled.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public ValueTask DisposeAsync()
        {
            _logger.LogStopAcceptingConnections(ServerAddress);
            return _decoratee.DisposeAsync();
        }

        internal LogAndRetryConnectorListenerDecorator(IConnectorListener decoratee, ILogger? logger)
        {
            _decoratee = decoratee;
            _logger = logger ?? NullLogger.Instance;
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
                _remoteNetworkAddress,
                _logger);

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public Task RefuseTransportConnectionAsync(CancellationToken cancel) =>
            _decoratee.RefuseTransportConnectionAsync(cancel);

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
            ServerMetrics.Instance.ConnectionStart();
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
            ServerMetrics.Instance.ConnectStart();
            try
            {
                return await _decoratee.ConnectTransportConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ServerMetrics.Instance.ConnectStop();
                ServerMetrics.Instance.ConnectionStop();
                throw;
            }
        }

        public IProtocolConnection CreateProtocolConnection(
            TransportConnectionInformation transportConnectionInformation) =>
                new MetricsProtocolConnectionDecorator(
                    _decoratee.CreateProtocolConnection(transportConnectionInformation));

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public async Task RefuseTransportConnectionAsync(CancellationToken cancel)
        {
            try
            {
                await _decoratee.RefuseTransportConnectionAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                ServerMetrics.Instance.ConnectStop();
                ServerMetrics.Instance.ConnectionStop();
            }
        }

        internal MetricsConnectorDecorator(IConnector decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds metrics to the <see cref="IProtocolConnection" />.</summary>
    private class MetricsProtocolConnectionDecorator : IProtocolConnection
    {
        public Task<Exception?> Closed => _decoratee.Closed;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownRequested => _decoratee.ShutdownRequested;

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
            catch
            {
                ServerMetrics.Instance.ConnectionStop();
                throw;
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

        public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

        internal MetricsProtocolConnectionDecorator(IProtocolConnection decoratee)
        {
            _decoratee = decoratee;

            _shutdownTask = ShutdownAsync();

            // This task executes once per decorated connection.
            async Task ShutdownAsync()
            {
                if (await Closed.ConfigureAwait(false) is not null)
                {
                    ServerMetrics.Instance.ConnectionFailure();
                }
                ServerMetrics.Instance.ConnectionStop();
            }
        }
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

        Task RefuseTransportConnectionAsync(CancellationToken cancel);
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

        public Task RefuseTransportConnectionAsync(CancellationToken cancel)
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

        public async Task<(IConnector, EndPoint)> AcceptAsync(CancellationToken cancel)
        {
            (IMultiplexedConnection transportConnection, EndPoint remoteNetworkAddress) = await _listener.AcceptAsync(
                cancel).ConfigureAwait(false);
            return (new IceRpcConnector(transportConnection, _options), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();

        internal IceRpcConnectorListener(IListener<IMultiplexedConnection> listener, ConnectionOptions options)
        {
            _listener = listener;
            _options = options;
        }
    }

    private class IceRpcConnector : IConnector
    {
        private readonly ConnectionOptions _options;
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
                _options);
            _transportConnection = null;
            return protocolConnection;
        }

        public ValueTask DisposeAsync() => _transportConnection?.DisposeAsync() ?? new();

        public Task RefuseTransportConnectionAsync(CancellationToken cancel) =>
            _transportConnection!.CloseAsync(MultiplexedConnectionCloseError.ServerBusy, cancel);

        internal IceRpcConnector(
            IMultiplexedConnection transportConnection,
            ConnectionOptions options)
        {
            _transportConnection = transportConnection;
            _options = options;
        }
    }
}
