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

    private IProtocolConnection CurrentProtocolConnection
    {
        get
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
    }

    // The underlying protocol connection
    private IProtocolConnection _connection;

    // The connection parameter represents the previous connection, if any.
    private readonly Func<IProtocolConnection?, IProtocolConnection> _connectionFactory;

    private bool _isDisposed;

    // Protects _connection and _isDisposed.
    private readonly object _mutex = new();

    private readonly CancellationTokenSource _shutdownCts = new();

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
        ServerAddress serverAddress = options.ServerAddress ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.ServerAddress)} is not set",
                nameof(options));

        IClientProtocolConnectionFactory protocolConnectionFactory = new ClientProtocolConnectionFactory(
            options,
            options.ClientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport,
            logger);

        _connectionFactory = previousConnection =>
        {
            IProtocolConnection connection = protocolConnectionFactory.CreateConnection(serverAddress);

            if (previousConnection is not null)
            {
                ValueTask valueTask = CleanupAsync(previousConnection);

                if (!valueTask.IsCompleted)
                {
                    // Add a decorator to wait for the cleanup of the previous connection in ConnectAsync/DisposeAsync.
                    connection = new CleanupProtocolConnectionDecorator(connection, valueTask.AsTask());
                }
            }

            connection = new ConnectProtocolConnectionDecorator(connection, _shutdownCts.Token);
            _ = FulfillShutdownRequestAsync(connection);

            return connection;

            static async ValueTask CleanupAsync(IProtocolConnection connection)
            {
                // We need to wait for Closed in case we've received a shutdown request for this connection but have not
                // fulfilled it yet.
                _ = await connection.Closed.ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            static async Task FulfillShutdownRequestAsync(IProtocolConnection connection)
            {
                await connection.ShutdownRequested.ConfigureAwait(false);
                try
                {
                    // Use shutdown timeout via decorator.
                    await connection.ShutdownAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore shutdown exceptions
                }
            }
        };

        _connection = _connectionFactory(null); // null because there is no previous connection
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
        return PerformConnectAsync(CurrentProtocolConnection);

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
                    // This can occasionally happen if the connection was just closed and disposed by this
                    // ClientConnection.
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosed)
                {
                    // Same as above, except DisposeAsync was not called yet on this closed connection.
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

        return PerformInvokeAsync(CurrentProtocolConnection);

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
                    // This can occasionally happen if the connection was just closed and disposed by this
                    // ClientConnection.
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosed)
                {
                    // Same as above, except DisposeAsync was not called yet on this closed connection.
                }
                // let other exceptions through

                connection = RefreshConnection(connection);
            }
        }
    }

    /// <summary>Gracefully shuts down the connection. The shutdown waits for pending invocations and dispatches to
    /// complete. For a speedier graceful shutdown, call <see cref="DisposeAsync" /> instead. It will cancel pending
    /// invocations and dispatches.</summary>
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
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <remarks>This method can be called multiple times. Only the first <paramref name="cancellationToken" /> has an
    /// effect on the underlying connection. If the first <paramref name="cancellationToken" /> is canceled, the
    /// connection is aborted.</remarks>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        IProtocolConnection connection;
        lock (_mutex)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException($"{typeof(ClientConnection)}");
            }

            _shutdownCts.Cancel();
            connection = _connection;
        }

        return connection.ShutdownAsync(cancellationToken);
    }

    /// <summary>Refreshes _connection and returns the latest _connection.</summary>
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
                _connection = _connectionFactory(connection);
            }
            return _connection;
        }
    }

    // A decorator that awaits a cleanup task (= previous connection dispose) in ConnectAsync and DisposeAsync.
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

        public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(cancellationToken);

        internal CleanupProtocolConnectionDecorator(IProtocolConnection decoratee, Task cleanupTask)
        {
            _cleanupTask = cleanupTask;
            _decoratee = decoratee;
        }
    }

    /// <summary>Provides a decorator for <see cref="IProtocolConnection" /> that ensures <see cref="InvokeAsync" />
    /// calls <see cref="ConnectAsync" /> when the connection is not connected yet. Its <see cref="DisposeAsync" /> also
    /// performs a graceful shutdown if <see cref="ConnectAsync" /> was called and completed successfully. This
    /// decorator allows multiple calls to <see cref="ConnectAsync" /> and <see cref="ShutdownAsync" />.</summary>
    private class ConnectProtocolConnectionDecorator : IProtocolConnection
    {
        public Task<Exception?> Closed => _decoratee.Closed;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownRequested => _decoratee.ShutdownRequested;

        private Task<TransportConnectionInformation>? _connectTask;

        private readonly IProtocolConnection _decoratee;

        // Set to true once the connection is successfully connected. It's not volatile or protected by mutex: in the
        // unlikely event the caller sees false after the connection is connected, it will call ConnectAsync and succeed
        // immediately.
        private bool _isConnected;

        private readonly object _mutex = new();

        // canceled when ClientConnection is shutting down.
        private readonly CancellationToken _shutdownCancellationToken;

        private Task? _shutdownTask;

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            lock (_mutex)
            {
                if (_connectTask is null)
                {
                    _connectTask = PerformConnectAsync();
                    return _connectTask;
                }
                else if (_connectTask.IsFaulted)
                {
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

                TransportConnectionInformation connectionInfo;
                try
                {
                    connectionInfo = await _decoratee.ConnectAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was canceled by the shutdown of the client connection.");
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
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was canceled.");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            bool waitForShutdown = false;

            lock (_mutex)
            {
                if (_connectTask is not null && _connectTask.IsCompletedSuccessfully)
                {
                    // Perform graceful shutdown
                    _shutdownTask ??= PerformShutdownAsync();
                    waitForShutdown = true;
                }
            }

            if (waitForShutdown)
            {
                _ = await Closed.ConfigureAwait(false);
            }

            await _decoratee.DisposeAsync().ConfigureAwait(false);

            async Task PerformShutdownAsync()
            {
                await Task.Yield(); // exit mutex

                // Attempt a graceful shutdown and ignore any exception to avoid UTE.
                try
                {
                    await _decoratee.ShutdownAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
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

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            lock (_mutex)
            {
                if (_shutdownTask is null)
                {
                    _shutdownTask = PerformShutdownAsync();
                    return _shutdownTask;
                }
            }
            return WaitForShutdownAsync(_shutdownTask);

            async Task PerformShutdownAsync()
            {
                await Task.Yield(); // exit mutex
                await _decoratee.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }

            // Wait for the first shutdown call to complete.
            async Task WaitForShutdownAsync(Task shutdownTask)
            {
                try
                {
                    await shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IceRpcException(IceRpcError.OperationAborted, "The connection shutdown was canceled.");
                }
            }
        }

        internal ConnectProtocolConnectionDecorator(
            IProtocolConnection decoratee,
            CancellationToken shutdownCancellationToken)
        {
            _decoratee = decoratee;
            _shutdownCancellationToken = shutdownCancellationToken;
        }
    }
}
