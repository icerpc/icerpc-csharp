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

/// <summary>A server serves clients by listening for the requests they send, processing these requests and sending the
/// corresponding responses.</summary>
public sealed class Server : IAsyncDisposable
{
    private int _backgroundConnectionDisposeCount;

    private readonly TaskCompletionSource _backgroundConnectionDisposeTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly HashSet<IProtocolConnection> _connections = new();

    private IConnectorListener? _listener;

    // We keep this task to await the completion of listener's DisposeAsync when making concurrent calls to
    // ShutdownAsync and/or DisposeAsync.
    private Task? _listenerDisposeTask;

    private readonly Func<IConnectorListener> _listenerFactory;

    private Task? _listenTask;

    private readonly int _maxConnections;

    private readonly int _maxPendingConnections;

    // protects _listener and _connections
    private readonly object _mutex = new();

    private readonly ServerAddress _serverAddress;

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

        duplexServerTransport ??= IDuplexServerTransport.Default;
        multiplexedServerTransport ??= IMultiplexedServerTransport.Default;
        _maxConnections = options.MaxConnections;
        _maxPendingConnections = options.MaxPendingConnections;

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
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        if (_listenerDisposeTask is not null)
        {
            await _listenerDisposeTask.ConfigureAwait(false);
        }

        await Task.WhenAll(_connections.Select(c => c.DisposeAsync().AsTask())
            .Append(_backgroundConnectionDisposeTcs.Task)).ConfigureAwait(false);

        _shutdownCts.Dispose();
    }

    /// <summary>Starts listening on the configured server address and dispatching requests from clients.</summary>
    /// <returns>The server address this server is listening on and that a client would connect to. This address is the
    /// same as <see cref="ServerOptions.ServerAddress" /> except its <see cref="ServerAddress.Transport" /> property is
    /// always non-null and its port number is never 0 when the host is an IP address.</returns>
    /// <exception cref="IceRpcException">Thrown when another server is already listening on the same server address.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or shutting
    /// down.</exception>
    public ServerAddress Listen()
    {
        CancellationToken shutdownCancellationToken;
        IConnectorListener listener;

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
                throw new InvalidOperationException($"Server '{this}' is shut down or shutting down.");
            }
            if (_listener is not null)
            {
                throw new InvalidOperationException($"Server '{this}' is already listening.");
            }

            listener = _listenerFactory();
            _listener = listener;
        }

        _listenTask = Task.Run(async () =>
        {
            try
            {
                using var pendingConnectionSemaphore = new SemaphoreSlim(
                    _maxPendingConnections,
                    _maxPendingConnections);

                while (!shutdownCancellationToken.IsCancellationRequested)
                {
                    await pendingConnectionSemaphore.WaitAsync(shutdownCancellationToken).ConfigureAwait(false);

                    (IConnector connector, _) = await listener.AcceptAsync(shutdownCancellationToken)
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
                                // Ignore connection establishment failure. This failures are logged by the
                                // LogConnectorDecorator
                            }
                            finally
                            {
                                // The connection dispose will dispose the transport connection if it has not been
                                // adopted by the protocol connection.
                                await connector.DisposeAsync().ConfigureAwait(false);
                                if (!shutdownCancellationToken.IsCancellationRequested)
                                {
                                    pendingConnectionSemaphore.Release();
                                }
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
            catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
            {
                // The AcceptAsync call can fail with OperationAborted during shutdown if it is accepting a connection
                // while the listener is disposed.
            }
            // other exceptions thrown by listener.AcceptAsync are logged by listener via a log decorator
        });

        return listener.ServerAddress;

        async Task ConnectAsync(IConnector connector)
        {
            // Connect the transport connection first.
            TransportConnectionInformation transportConnectionInformation =
                await connector.ConnectTransportConnectionAsync(shutdownCancellationToken).ConfigureAwait(false);

            // Create the protocol connection if the server is not being shutdown and if the max connection count is not
            // reached.
            IProtocolConnection? protocolConnection = null;
            lock (_mutex)
            {
                if (!shutdownCancellationToken.IsCancellationRequested &&
                    (_maxConnections == 0 || _connections.Count < _maxConnections))
                {
                    // The protocol connection adopts the transport connection from the connector and it's not
                    // responsible for disposing of it.
                    protocolConnection = connector.CreateProtocolConnection(transportConnectionInformation);
                    _connections.Add(protocolConnection);
                }
            }

            if (protocolConnection is not null)
            {
                // Schedule removal after addition, outside mutex lock.
                _ = RemoveFromCollectionAsync(protocolConnection, shutdownCancellationToken);

                // Connect the connection. Don't need to pass a cancellation token here ConnectAsync creates one with
                // the configured connection timeout.
                await protocolConnection.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else if (!shutdownCancellationToken.IsCancellationRequested)
            {
                // If the max connection count is reached, we refuse the transport connection. We don't pass a
                // cancellation token here. The transport is responsible for ensuring that CloseAsync fails if the peer
                // doesn't acknowledge the failure.
                try
                {
                    await connector.RefuseTransportConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignore and continue
                }
            }
            // The transport connection is disposed by the disposal of the connector if the protocol connection didn't
            // adopt it above.
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
    /// <returns>A task that completes successfully once the shutdown is complete.</returns>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
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

        if (_listenTask is not null)
        {
            // The listen task should not throw any exception, but if it does, the application will get this exception
            // when it calls ShutdownAsync.
            await _listenTask.ConfigureAwait(false);
        }

        if (_listenerDisposeTask is not null)
        {
            await _listenerDisposeTask.ConfigureAwait(false);
        }

        await Task.WhenAll(_connections.Select(entry => entry.ShutdownAsync(cancellationToken))).ConfigureAwait(false);
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

        public Task RefuseTransportConnectionAsync(CancellationToken cancel) =>
            _transportConnection!.ShutdownAsync(cancel);

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
