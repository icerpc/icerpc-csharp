// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection is
/// reconnected automatically when its underlying connection is closed by the server or the transport. Only one
/// underlying connection can be established, connecting or shutting down at any one time.</summary>
public sealed class ClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport" /> property is always
    /// non-null.</value>
    public ServerAddress ServerAddress => _connection.ServerAddress;

    // The underlying protocol connection
    private IProtocolConnection _connection;

    // The task that shuts down _connection (if requested) and disposes the _connection. This task is never faulted
    // and is no-op once this ClientConnection is shutdown or disposed.
    private Task _connectionCleanupTask;

    private readonly Func<IProtocolConnection> _connectionFactory;

    private bool _isDisposed;

    // Protects _connection and _isDisposed.
    private readonly object _mutex = new();

    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly TimeSpan _shutdownTimeout;

    /// <summary>Constructs a client connection.</summary>
    /// <param name="options">The client connection options.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice connections.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc connections.</param>
    /// <param name="logger">The logger.</param>
    public ClientConnection(
        ClientConnectionOptions options,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        ILogger? logger = null)
    {
        _shutdownTimeout = options.ShutdownTimeout;

        ServerAddress serverAddress = options.ServerAddress ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.ServerAddress)} is not set",
                nameof(options));

        var clientProtocolConnectionFactory = new ClientProtocolConnectionFactory(
            options,
            options.ClientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport,
            logger);

        _connectionFactory = () =>
            new ConnectProtocolConnectionDecorator(
                    clientProtocolConnectionFactory.CreateConnection(serverAddress),
                    options.ConnectTimeout,
                    _shutdownCts.Token);

        _connection = _connectionFactory();
        _connectionCleanupTask = CreateConnectionCleanupTask(_connection, _shutdownCts.Token);
    }

    /// <summary>Constructs a resumable client connection with the specified server address and client authentication
    /// options. All other <see cref="ClientConnectionOptions" /> properties have their default values.</summary>
    /// <param name="serverAddress">The connection's server address.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice connections.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc connections.</param>
    /// <param name="logger">The logger.</param>
    public ClientConnection(
        ServerAddress serverAddress,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        ILogger? logger = null)
        : this(
            new ClientConnectionOptions
            {
                ClientAuthenticationOptions = clientAuthenticationOptions,
                ServerAddress = serverAddress
            },
            duplexClientTransport,
            multiplexedClientTransport,
            logger)
    {
    }

    /// <summary>Constructs a resumable client connection with the specified server address URI and client
    /// authentication options. All other <see cref="ClientConnectionOptions" /> properties have their default values.
    /// </summary>
    /// <param name="serverAddressUri">The connection's server address URI.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice connections.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc connections.</param>
    /// <param name="logger">The logger.</param>
    public ClientConnection(
        Uri serverAddressUri,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        ILogger? logger = null)
        : this(
            new ServerAddress(serverAddressUri),
            clientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport,
            logger)
    {
    }

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that provides the <see cref="TransportConnectionInformation" /> of the transport connection,
    /// once this connection is established. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the connection establishment failed.</description>
    /// </item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if this connection attempt or a previous attempt exceeded
    /// <see cref="ConnectionOptions.ConnectTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown if this client connection is disposed.</exception>
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return PerformConnectAsync(GetCurrentProtocolConnection());

        async Task<TransportConnectionInformation> PerformConnectAsync(IProtocolConnection connection)
        {
            while (true)
            {
                try
                {
                    return await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // This can happen if a previous ConnectAsync failed and this failed connection was disposed.
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosed)
                {
                    // Same as above except the connection is not disposed yet.
                }

                connection = RefreshConnection(connection);
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _shutdownCts.Cancel();
                _shutdownCts.Dispose();
            }
        }
        return _connection.DisposeAsync();
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Features.Get<IServerAddressFeature>() is IServerAddressFeature serverAddressFeature)
        {
            if (serverAddressFeature.ServerAddress is ServerAddress mainServerAddress)
            {
                CheckRequestServerAddresses(mainServerAddress, serverAddressFeature.AltServerAddresses);
            }
        }
        else if (request.ServiceAddress.ServerAddress is ServerAddress mainServerAddress)
        {
            CheckRequestServerAddresses(mainServerAddress, request.ServiceAddress.AltServerAddresses);
        }
        // It's ok if the request has no server address at all.

        return PerformInvokeAsync(GetCurrentProtocolConnection());

        void CheckRequestServerAddresses(
            ServerAddress mainServerAddress,
            ImmutableList<ServerAddress> altServerAddresses)
        {
            if (ServerAddressComparer.OptionalTransport.Equals(mainServerAddress, ServerAddress))
            {
                return;
            }

            foreach (ServerAddress serverAddress in altServerAddresses)
            {
                if (ServerAddressComparer.OptionalTransport.Equals(serverAddress, ServerAddress))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"None of the request's server addresses matches this connection's server address: {ServerAddress}");
        }

        async Task<IncomingResponse> PerformInvokeAsync(IProtocolConnection connection)
        {
            while (true)
            {
                try
                {
                    return await connection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // This can happen if a previous ConnectAsync or InvokeAsync failed and this failed connection was
                    // disposed.
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosed)
                {
                    // Same as above except the connection is not disposed yet.
                }
                // let other exceptions through

                connection = RefreshConnection(connection);
            }
        }
    }

    /// <summary>Gracefully shuts down the connection. The shutdown waits for pending invocations and dispatches to
    /// complete.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the connection shutdown failed.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if this shutdown attempt or a previous attempt exceeded <see
    /// cref="ConnectionOptions.ShutdownTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="IceRpcException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="InvalidOperationException">Thrown if this connection is already shut down or shutting down.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException($"{typeof(ClientConnection)}");
            }
            if (_shutdownCts.IsCancellationRequested)
            {
                throw new InvalidOperationException("The client connection is already shut down or shutting down.");
            }

            _shutdownCts.Cancel();
        }

        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_shutdownTimeout);

            try
            {
                // _connection is immutable once _shutdownCts is canceled.
                await _connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"The client connection shut down timed out after {_shutdownTimeout.TotalSeconds} s.");
            }
        }
    }

    private async Task CreateConnectionCleanupTask(
        IProtocolConnection connection,
        CancellationToken shutdownCancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        bool shutdownRequested;

        try
        {
            shutdownRequested = await Task.WhenAny(connection.ShutdownRequested, connection.Closed)
                .WaitAsync(shutdownCancellationToken).ConfigureAwait(false) == connection.ShutdownRequested;
        }
        catch (OperationCanceledException)
        {
            // The client connection is being shut down. We check what to do below after locking _mutex.
            shutdownRequested = false;
        }
        // no other exception can be thrown

        lock (_mutex)
        {
            if (connection == _connection)
            {
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // The client connection is being shut down or disposed its ShutdownAsync/DisposeAsync will
                    // shutdown/dispose _connection.
                    return;
                }

                // Make sure connection becomes ours only since we're going to shut it down or dispose it (or both).
                // A call to ClientConnection.ShutdownAsync that acquires mutex immediately after we release it should
                // not see a _connection that is shut down or disposed.
                _ = RefreshConnection(connection);
            }
        }

        if (shutdownRequested)
        {
            using var cts = new CancellationTokenSource(_shutdownTimeout);

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
            }
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private IProtocolConnection GetCurrentProtocolConnection()
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException($"{typeof(ClientConnection)}");
            }
            if (_shutdownCts.IsCancellationRequested)
            {
                throw new IceRpcException(IceRpcError.ConnectionClosed, "The client connection is shut down.");
            }
            return _connection;
        }
    }

    /// <summary>Refreshes _connection and _connectionCleanupTask and returns the latest _connection.</summary>
    private IProtocolConnection RefreshConnection(IProtocolConnection connection)
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                throw new IceRpcException(IceRpcError.OperationAborted, "The client connection is disposed.");
            }
            if (_shutdownCts.IsCancellationRequested)
            {
                throw new IceRpcException(IceRpcError.ConnectionClosed, "The client connection is shut down.");
            }

            // We only create a new connection and assign it to _connection if it matches the connection we just tried.
            // If it's another connection, another thread has already called RefreshConnection.
            if (connection == _connection)
            {
                _connection = _connectionFactory();

                // We create a decorator with the cleanup task of the previous connection. ConnectAsync (and
                // DisposeAsync if ConnectAsync is never called) first waits for this cleanup task to complete.
                // Since RefreshConnection is typically called by _connectionCleanupTask, the cleanup task is typically
                // not completed at this point.
                _connection = new CleanupProtocolConnectionDecorator(_connection, _connectionCleanupTask);

                // Immediately create cleanup task for new connection.
                _connectionCleanupTask = CreateConnectionCleanupTask(_connection, _shutdownCts.Token);
            }
            return _connection;
        }
    }

    // A decorator that awaits a cleanup task (= previous connection shutdown/dispose) in ConnectAsync and DisposeAsync.
    // This cleanup task is never faulted or canceled.
    private class CleanupProtocolConnectionDecorator : IProtocolConnection
    {
        public Task<Exception?> Closed => _decoratee.Closed;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownRequested => _decoratee.ShutdownRequested;

        private readonly Task _cleanupTask;

        private readonly IProtocolConnection _decoratee;

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            return _cleanupTask.IsCompleted ? _decoratee.ConnectAsync(cancellationToken) : PerformConnectAsync();

            async Task<TransportConnectionInformation> PerformConnectAsync()
            {
                await _cleanupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                return await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _cleanupTask.IsCompleted ? _decoratee.DisposeAsync() : PerformDisposeAsync();

            async ValueTask PerformDisposeAsync()
            {
                await _cleanupTask.ConfigureAwait(false);
                await _decoratee.DisposeAsync().ConfigureAwait(false);
            }
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

        internal CleanupProtocolConnectionDecorator(IProtocolConnection decoratee, Task cleanupTask)
        {
            _cleanupTask = cleanupTask;
            _decoratee = decoratee;
        }
    }

    /// <summary>Provides a decorator for <see cref="IProtocolConnection" /> that ensures <see cref="InvokeAsync" />
    /// calls <see cref="ConnectAsync" /> when the connection is not connected yet. This decorator also implements the
    /// ConnectTimeout allows multiple calls to <see cref="ConnectAsync" />.</summary>
    private class ConnectProtocolConnectionDecorator : IProtocolConnection
    {
        public Task<Exception?> Closed => _decoratee.Closed;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownRequested => _decoratee.ShutdownRequested;

        private Task<TransportConnectionInformation>? _connectTask;

        private readonly TimeSpan _connectTimeout;

        private readonly IProtocolConnection _decoratee;

        // Set to true once the connection is successfully connected. It's not volatile or protected by mutex: in the
        // unlikely event the caller sees false after the connection is connected, it will call ConnectAsync and succeed
        // immediately.
        private bool _isConnected;

        private bool _isDisposed;

        private readonly object _mutex = new();

        // canceled when ClientConnection is shutting down.
        private readonly CancellationToken _shutdownCancellationToken;

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            lock (_mutex)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException($"{typeof(ConnectProtocolConnectionDecorator)}");
                }

                if (_connectTask is null)
                {
                    _connectTask = PerformConnectAsync();
                    return _connectTask;
                }
                else if (_connectTask.IsFaulted || _connectTask.IsCanceled)
                {
                    // If a previous ConnectAsync on this connection failed, we want this connection to be discarded
                    // and replaced.
                    throw new IceRpcException(IceRpcError.ConnectionClosed);
                }
            }
            return WaitForConnectAsync(_connectTask);

            async Task<TransportConnectionInformation> PerformConnectAsync()
            {
                await Task.Yield(); // exit mutex

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _shutdownCancellationToken);

                cts.CancelAfter(_connectTimeout);

                TransportConnectionInformation connectionInfo;
                try
                {
                    connectionInfo = await _decoratee.ConnectAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_shutdownCancellationToken.IsCancellationRequested)
                    {
                        throw new IceRpcException(
                            IceRpcError.OperationAborted,
                            "The connection establishment was canceled by the shutdown of the client connection.");
                    }
                    throw new TimeoutException(
                        $"The connection establishment timed out after {_connectTimeout.TotalSeconds} s.");
                }
                _isConnected = true;
                return connectionInfo;
            }

            // Wait for the first connect call to complete
            async Task<TransportConnectionInformation> WaitForConnectAsync(
                Task<TransportConnectionInformation> connectTask)
            {
                try
                {
                    return await connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken != cancellationToken)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was canceled.");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_mutex)
            {
                _isDisposed = true;
            }

            await _decoratee.DisposeAsync().ConfigureAwait(false);

            // _connectTask is readonly once _isDisposed is true.
            _ = _connectTask?.Exception; // observe exception in case _connectTask is faulted.
        }

        public Task<IncomingResponse> InvokeAsync(
            OutgoingRequest request,
            CancellationToken cancellationToken = default)
        {
            return _isConnected ? _decoratee.InvokeAsync(request, cancellationToken) : PerformConnectInvokeAsync();

            async Task<IncomingResponse> PerformConnectInvokeAsync()
            {
                // Perform the connection establishment without a cancellation token. It will timeout if the connect
                // timeout is reached.
                _ = await ConnectAsync(CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false);
                return await _decoratee.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

        internal ConnectProtocolConnectionDecorator(
            IProtocolConnection decoratee,
            TimeSpan connectTimeout,
            CancellationToken shutdownCancellationToken)
        {
            _decoratee = decoratee;
            _connectTimeout = connectTimeout;
            _shutdownCancellationToken = shutdownCancellationToken;
        }
    }
}
