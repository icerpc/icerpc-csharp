// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
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

    // The connection parameter represents the previous connection, if any.
    private readonly Func<IProtocolConnection?, IProtocolConnection> _connectionFactory;

    private readonly object _mutex = new();

    // When true, we retry once when _connection is closed, i.e. a call on _connection throws a ConnectionException
    // with a Closed error code or ObjectDisposedException.
    private bool _retryOnClosed = true;

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
            multiplexedClientTransport);

        protocolConnectionFactory = new MetricsClientProtocolConnectionFactoryDecorator(protocolConnectionFactory);

        if (logger is not null)
        {
            protocolConnectionFactory = new LogClientProtocolConnectionFactoryDecorator(
                protocolConnectionFactory,
                logger);
        }

        _connectionFactory = previousConnection =>
        {
            IProtocolConnection connection = protocolConnectionFactory.CreateConnection(serverAddress);

            if (previousConnection is not null &&
                CleanupAsync(previousConnection) is Task cleanupTask &&
                !cleanupTask.IsCompleted)
            {
                // Add a decorator to wait for the cleanup of the previous connection in ConnectAsync/DisposeAsync.
                connection = new CleanupProtocolConnectionDecorator(connection, cleanupTask);
            }

            connection = new ConnectProtocolConnectionDecorator(connection);
            _ = RefreshOnClosedAsync(connection);

            return connection;

            static async Task CleanupAsync(IProtocolConnection connection)
            {
                try
                {
                    // For example, wait for the shutdown initiated by the peer to complete successfully.
                    await connection.ShutdownComplete.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            async Task RefreshOnClosedAsync(IProtocolConnection connection)
            {
                try
                {
                    await connection.ShutdownComplete.ConfigureAwait(false);
                }
                catch (ConnectionException exception) when (exception.ErrorCode.IsClosedErrorCode())
                {
                    // expected, call refresh below
                }
                catch
                {
                    return; // don't refresh
                }

                _ = RefreshConnection(connection);
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
    /// <item><description><see cref="ConnectionException" />if the connection establishment failed.</description>
    /// </item>
    /// <item><description><see cref="ObjectDisposedException" />if this connection is disposed.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if this connection attempt or a previous attempt exceeded
    /// <see cref="ConnectionOptions.ConnectTimeout" />.</description></item>
    /// </list>
    /// </returns>
    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Keep a reference to the connection we're trying to connect to.
        IProtocolConnection connection = _connection;

        try
        {
            return await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            if (RefreshConnection(connection) is IProtocolConnection newConnection)
            {
                // Try again once with the new connection
                return await newConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            throw;
        }
        catch (ConnectionException exception) when (exception.ErrorCode.IsClosedErrorCode())
        {
            if (RefreshConnection(connection) is IProtocolConnection newConnection)
            {
                // Try again once with the new connection
                return await newConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _retryOnClosed = false;
        }

        return _connection.DisposeAsync();
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
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

        // If the request has no server address at all, we let it through.

        return PerformInvokeAsync();

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
                $"none of the server addresses of the request matches this connection's server address: {ServerAddress}");
        }

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            IProtocolConnection connection = _connection;

            try
            {
                return await connection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                if (RefreshConnection(connection) is IProtocolConnection newConnection)
                {
                    // try again once with the new connection
                    return await newConnection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                throw;
            }
            catch (ConnectionException exception) when (exception.ErrorCode.IsClosedErrorCode())
            {
                if (RefreshConnection(connection) is IProtocolConnection newConnection)
                {
                    // try again once with the new connection
                    return await newConnection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                throw;
            }
        }
    }

    /// <summary>Gracefully shuts down the connection. The shutdown waits for pending invocations and dispatches to
    /// complete. For a speedier graceful shutdown, call <see cref="IAsyncDisposable.DisposeAsync" /> instead. It will
    /// cancel pending invocations and dispatches.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionException" />if the connection shutdown failed.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if this shutdown attempt or a previous attempt exceeded <see
    /// cref="ConnectionOptions.ShutdownTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ConnectionException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <remarks>If shutdown is canceled, the protocol connection transitions to a faulted state and the disposal of the
    /// connection will abort the connection instead of performing a graceful speedy-shutdown.</remarks>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            _retryOnClosed = false;
        }
        return _connection.ShutdownAsync(cancellationToken);
    }

    /// <summary>Refreshes _connection and returns the latest _connection, or null if ClientConnection is no longer
    /// resumable.</summary>
    private IProtocolConnection? RefreshConnection(IProtocolConnection connection)
    {
        IProtocolConnection? newConnection = null;

        lock (_mutex)
        {
            // We only create a new connection and assign it to _connection if it matches the connection we just tried.
            // If it's another connection, another thread has already called RefreshConnection.
            if (_retryOnClosed)
            {
                if (connection == _connection)
                {
                    _connection = _connectionFactory(connection);
                }
                newConnection = _connection;
            }
        }

        return newConnection;
    }

    // A decorator that awaits a cleanup task (= previous connection cleanup) in ConnectAsync and DisposeAsync.
    private class CleanupProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly Task _cleanupTask;

        private readonly IProtocolConnection _decoratee;

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            return _cleanupTask.IsCompleted ? _decoratee.ConnectAsync(cancellationToken) : PerformConnectAsync();

            async Task<TransportConnectionInformation> PerformConnectAsync()
            {
                await _cleanupTask.ConfigureAwait(false);
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

    /// <summary>Provides a decorator for <see cref="IProtocolConnection" /> that ensures
    /// <see cref="IInvoker.InvokeAsync" /> calls <see cref="IProtocolConnection.ConnectAsync" /> when the connection is
    /// not connected yet. This decorator also allows multiple and concurrent calls to
    /// <see cref="IProtocolConnection.ConnectAsync" />.</summary>
    /// <seealso cref="ClientProtocolConnectionFactory.CreateConnection" />
    private class ConnectProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownComplete => _decoratee.ShutdownComplete;

        private Task<TransportConnectionInformation>? _connectTask;

        private readonly IProtocolConnection _decoratee;

        // Set to true once the connection is successfully connected. It's not volatile or protected by mutex: in the
        // unlikely event the caller sees false after the connection is connected, it will call ConnectAsync and succeed
        // immediately.
        private bool _isConnected;

        private readonly object _mutex = new();

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            lock (_mutex)
            {
                if (_connectTask is null)
                {
                    _connectTask = PerformConnectAsync();
                    return _connectTask;
                }
            }
            return PerformWaitForConnectAsync();

            async Task<TransportConnectionInformation> PerformConnectAsync()
            {
                await Task.Yield(); // exit mutex lock

                TransportConnectionInformation connectionInformation = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                _isConnected = true;
                return connectionInformation;
            }

            async Task<TransportConnectionInformation> PerformWaitForConnectAsync()
            {
                // Only for second and subsequent attempts
                try
                {
                    return await _connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                {
                    throw;
                }
                catch
                {
                    // ShutdownComplete throws a ConnectionException with a Closed error code
                    await _decoratee.ShutdownComplete.ConfigureAwait(false);
                    throw;
                }
            }
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
        {
            return _isConnected ? _decoratee.InvokeAsync(request, cancellationToken) : PerformConnectInvokeAsync();

            async Task<IncomingResponse> PerformConnectInvokeAsync()
            {
                // Perform the connection establishment without a cancellation token. It will timeout if the connect
                // timeout is reached.
                _ = await ConnectAsync(CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false);
                return await InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(cancellationToken);

        internal ConnectProtocolConnectionDecorator(IProtocolConnection decoratee) => _decoratee = decoratee;
    }
}
