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

    private IListener? _listener;

    // We keep this task to await the completion of listener's DisposeAsync when making concurrent calls to
    // ShutdownAsync and/or DisposeAsync.
    private Task? _listenerDisposeTask;

    private readonly Func<IListener<IProtocolConnection>> _listenerFactory;

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

        _listenerFactory = () =>
        {
            // This is the composition root for the protocol and transport listeners.

            IListener<IProtocolConnection> listener;
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
                listener = new IceProtocolListener(options.ConnectionOptions, transportListener);
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
                listener = new IceRpcProtocolListener(options.ConnectionOptions, transportListener);
            }
            return new MetricsListenerDecorator(listener);
        };
    }

    /// <summary>Constructs a server with the specified dispatcher and authentication options. All other properties
    /// have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    public Server(
        IDispatcher dispatcher,
        SslServerAuthenticationOptions? authenticationOptions = null,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                }
            },
            duplexServerTransport,
            multiplexedServerTransport)
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddress">The server address of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    public Server(
        IDispatcher dispatcher,
        ServerAddress serverAddress,
        SslServerAuthenticationOptions? authenticationOptions = null,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                ServerAddress = serverAddress
            },
            duplexServerTransport,
            multiplexedServerTransport)
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address URI and authentication options. All
    /// other properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddressUri">A URI that represents the server address of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default" />.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default" />.</param>
    public Server(
        IDispatcher dispatcher,
        Uri serverAddressUri,
        SslServerAuthenticationOptions? authenticationOptions = null,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null)
        : this(
            dispatcher,
            new ServerAddress(serverAddressUri),
            authenticationOptions,
            duplexServerTransport,
            multiplexedServerTransport)
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
        IListener<IProtocolConnection> listener;

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
                    (IProtocolConnection connection, _) =
                        await listener.AcceptAsync(shutdownCancellationToken).ConfigureAwait(false);

                    Func<IProtocolConnection, CancellationToken, Task>? connectFunc;
                    lock (_mutex)
                    {
                        // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                        if (shutdownCancellationToken.IsCancellationRequested)
                        {
                            connectFunc = null;
                        }
                        else if (_maxConnections > 0 && _connections.Count == _maxConnections)
                        {
                            // We have too many connections and can't accept any more.
                            // Reject the underlying transport connection by disposing the protocol connection.
                            _backgroundConnectionDisposeCount++;
                            connectFunc = RefuseConnectionAsync;
                        }
                        else
                        {
                            _connections.Add(connection);
                            connectFunc = ConnectConnectionAsync;
                        }
                    }

                    if (connectFunc is null)
                    {
                        // TODO: should we transmit an error code in this situation, when using a multiplexed transport?
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        // We don't wait for the connection to be activated or shutdown. This could take a while for
                        // some transports such as TLS based transports where the handshake requires few round trips
                        // between the client and server.
                        // Waiting could also cause a security issue if the client doesn't respond to the connection
                        // initialization as we wouldn't be able to accept new connections in the meantime. The call
                        // will eventually timeout if the ConnectTimeout expires.
                        _ = Task.Run(() => _ = connectFunc(connection, shutdownCancellationToken));
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
                // TODO log this exception
                _shutdownCompleteSource.TrySetException(exception);
            }
        });

        async Task ConnectConnectionAsync(IProtocolConnection connection, CancellationToken shutdownCancellationToken)
        {
            // Schedule removal after addition, outside mutex lock.
            _ = RemoveFromCollectionAsync(connection, shutdownCancellationToken);

            // Connect the connection. Don't need to pass a cancellation token here ConnectAsync creates one with the
            // configured connection timeout.
            await connection.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        async Task RefuseConnectionAsync(IProtocolConnection connection, CancellationToken shutdownCancellationToken)
        {
            // We need to call ShutdownAsync (and not simply DisposeAsync) to transmit the correct IceRPC error code
            // over the underlying multiplexed connection.
            try
            {
                await connection.ShutdownAsync(shutdownCancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore and continue
            }

            _ = BackgroundConnectionDisposeAsync(connection, shutdownCancellationToken);
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
            IProtocolConnection connection,
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

    /// <summary>Provides a decorator that adds metrics to a <see cref="IListener{T}" /> of
    /// <see cref="IProtocolConnection" />.</summary>
    private class MetricsListenerDecorator : IListener<IProtocolConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IProtocolConnection> _decoratee;

        public async Task<(IProtocolConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IProtocolConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            ServerMetrics.Instance.ConnectionStart();
            return (new MetricsProtocolConnectionDecorator(connection), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal MetricsListenerDecorator(IListener<IProtocolConnection> decoratee) => _decoratee = decoratee;
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
            ServerMetrics.Instance.ConnectStart();
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
