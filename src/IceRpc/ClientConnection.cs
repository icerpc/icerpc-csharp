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
    public ServerAddress ServerAddress { get; }

    // The underlying protocol connection once successfully established.
    private (IProtocolConnection Connection, TransportConnectionInformation ConnectionInformation)? _activeConnection;

    private readonly Func<IProtocolConnection> _connectionFactory;

    private readonly TimeSpan _connectTimeout;

    // A detached connection is a protocol connection that is shutting down or being disposed. Both ShutdownAsync and
    // DisposeAsync wait for detached connections to reach 0 using _detachedConnectionsTcs. Such a connection is
    // "detached" because it's not in _activeConnection or _pendingConnection.
    private int _detachedConnectionCount;

    private readonly TaskCompletionSource _detachedConnectionsTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A cancellation token source that is canceled when DisposeAsync is called.
    private readonly CancellationTokenSource _disposedCts = new();
    private Task? _disposeTask;

    private readonly object _mutex = new();

    // A connection being established and its associated connect task. When non-null, _activeConnection is null.
    private (IProtocolConnection Connection, Task<TransportConnectionInformation> ConnectTask)? _pendingConnection;

    private Task? _shutdownTask;

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
        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;

        duplexClientTransport ??= IDuplexClientTransport.Default;
        multiplexedClientTransport ??= IMultiplexedClientTransport.Default;

        ServerAddress = options.ServerAddress ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.ServerAddress)} is not set",
                nameof(options));

        if (ServerAddress.Transport is null)
        {
            ServerAddress = ServerAddress with
            {
                Transport = ServerAddress.Protocol == Protocol.Ice ?
                    duplexClientTransport.Name : multiplexedClientTransport.Name
            };
        }

        var clientProtocolConnectionFactory = new ClientProtocolConnectionFactory(
            options,
            options.ClientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport,
            logger);

        _connectionFactory = () => clientProtocolConnectionFactory.CreateConnection(ServerAddress);
    }

    /// <summary>Constructs a client connection with the specified server address and client authentication options. All
    /// other <see cref="ClientConnectionOptions" /> properties have their default values.</summary>
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

    /// <summary>Constructs a client connection with the specified server address URI and client authentication options.
    /// All other <see cref="ClientConnectionOptions" /> properties have their default values.</summary>
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
    /// <see cref="ClientConnectionOptions.ConnectTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if this client connection is shut down or shutting down.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown if this client connection is disposed.</exception>
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Task<TransportConnectionInformation> connectTask;

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ClientConnection)}");
            }
            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException("Cannot connect a client connection after shutting it down.");
            }

            if (_activeConnection is not null)
            {
                return Task.FromResult(_activeConnection.Value.ConnectionInformation);
            }

            if (_pendingConnection is null)
            {
                IProtocolConnection newConnection = _connectionFactory();
                connectTask = CreateConnectTask(newConnection, _disposedCts.Token, cancellationToken);
                _pendingConnection = (newConnection, connectTask);
            }
            else
            {
                connectTask = _pendingConnection.Value.ConnectTask.WaitAsync(cancellationToken);
            }
        }

        return PerformConnectAsync();

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            try
            {
                return await connectTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Canceled via the cancellation token given to ConnectAsync, but not necessarily this ConnectAsync
                // call.

                cancellationToken.ThrowIfCancellationRequested();

                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "The connection establishment was canceled by another concurrent attempt.");
            }
        }
    }

    /// <inheritdoc/>
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
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            await Task.Yield(); // Exit mutex lock

            _disposedCts.Cancel();

            if (_pendingConnection is not null)
            {
                await _pendingConnection.Value.Connection.DisposeAsync().ConfigureAwait(false);
            }
            if (_activeConnection is not null)
            {
                await _activeConnection.Value.Connection.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                await Task.WhenAll(
                    _shutdownTask,
                    _detachedConnectionsTcs.Task).ConfigureAwait(false);
            }
            catch
            {
                // ignore exceptions
            }

            _disposedCts.Dispose();
        }
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

        IProtocolConnection? activeConnection = null;

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ClientConnection)}");
            }
            if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, "The client connection was shut down.");
            }

            activeConnection = _activeConnection?.Connection;
        }

        return PerformInvokeAsync(activeConnection);

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

        async Task<IncomingResponse> PerformInvokeAsync(IProtocolConnection? connection)
        {
            connection ??= await GetActiveConnectionAsync(cancellationToken).ConfigureAwait(false);

            // When InvokeAsync throws an IceRpcException(InvocationRefused) we retry unless the client connection is
            // being shutdown.
            while (true)
            {
                try
                {
                    return await connection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // This can occasionally happen if we find a connection that was just closed and then automatically
                    // disposed by this client connection.
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.InvocationRefused)
                {
                    // The connection is refusing new invocations.
                }

                // Make sure connection is detached before we retry; we don't want to retry with the same connection.
                _ = DisposeActiveConnectionAsync(connection, shutdownRequested: true);

                connection = await GetActiveConnectionAsync(cancellationToken).ConfigureAwait(false);
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
    /// cref="ClientConnectionOptions.ShutdownTimeout" />.</description></item>
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
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ClientConnection)}");
            }
            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException("The client connection is already shut down or shutting down.");
            }

            if (_detachedConnectionCount == 0)
            {
                _ = _detachedConnectionsTcs.TrySetResult();
            }

            _shutdownTask = PerformShutdownAsync();
            return _shutdownTask;
        }

        async Task PerformShutdownAsync()
        {
            await Task.Yield(); // exit mutex lock

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposedCts.Token);
            cts.CancelAfter(_shutdownTimeout);

            try
            {
                if (_pendingConnection is not null)
                {
                    await _pendingConnection.Value.Connection.ShutdownPendingAsync(
                        _pendingConnection.Value.ConnectTask,
                        cts.Token).ConfigureAwait(false);
                }
                if (_activeConnection is not null)
                {
                    await _activeConnection.Value.Connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
                }

                await _detachedConnectionsTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_disposedCts.IsCancellationRequested)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The shutdown was aborted because the client connection was disposed.");
                }
                else
                {
                    throw new TimeoutException(
                        $"The client connection shut down timed out after {_shutdownTimeout.TotalSeconds} s.");
                }
            }
            catch
            {
                // ignore other shutdown exception
            }
        }
    }

    private async Task<TransportConnectionInformation> CreateConnectTask(
        IProtocolConnection connection,
        CancellationToken disposedCancellationToken,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposedCancellationToken);
        cts.CancelAfter(_connectTimeout);

        TransportConnectionInformation connectionInformation;

        try
        {
            try
            {
                connectionInformation = await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (disposedCancellationToken.IsCancellationRequested)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was aborted because ClientConnection was disposed.");
                }
                else
                {
                    throw new TimeoutException(
                        $"The connection establishment timed out after {_connectTimeout.TotalSeconds} s.");
                }
            }
        }
        catch
        {
            Task? connectTask = null;

            lock (_mutex)
            {
                Debug.Assert(_pendingConnection is not null && _pendingConnection.Value.Connection == connection);
                Debug.Assert(_activeConnection is null);

                if (_shutdownTask is null)
                {
                    // That's the task currently executing this method and about to throw. We need to observe this
                    // exception once the connectTask is completed.
                    connectTask = _pendingConnection.Value.ConnectTask;
                    _pendingConnection = null;
                    _detachedConnectionCount++;
                }
                // else ClientConnection.DisposeAsync will dispose this "attached" pending connection
            }

            if (connectTask is not null)
            {
                _ = DisposeFailedPendingConnectionAsync(connection, connectTask);
            }
            throw;
        }

        lock (_mutex)
        {
            Debug.Assert(_pendingConnection is not null && _pendingConnection.Value.Connection == connection);
            Debug.Assert(_activeConnection is null);

            if (_disposeTask is not null)
            {
                throw new IceRpcException(IceRpcError.OperationAborted, "The client connection was disposed.");
            }
            if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.OperationAborted, "The client connection was shut down.");
            }

            _activeConnection = (connection, connectionInformation);
            _pendingConnection = null;
        }

        _ = RemoveFromActiveAsync();
        return connectionInformation;

        async Task RemoveFromActiveAsync()
        {
            bool shutdownRequested =
                await Task.WhenAny(connection.ShutdownRequested, connection.Closed).ConfigureAwait(false) ==
                    connection.ShutdownRequested;

            await DisposeActiveConnectionAsync(connection, shutdownRequested).ConfigureAwait(false);
        }
    }

    private async Task DisposeActiveConnectionAsync(IProtocolConnection connection, bool shutdownRequested)
    {
        CancellationToken cancellationToken;
        lock (_mutex)
        {
            if (_shutdownTask is null && _activeConnection?.Connection == connection)
            {
                _activeConnection = null; // it's now our connection.
                _detachedConnectionCount++;
                cancellationToken = _disposedCts.Token;
            }
            else
            {
                // Another task owns this connection
                return;
            }
        }

        if (shutdownRequested)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_shutdownTimeout);

            try
            {
                await connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore connection shutdown failures
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

    private async Task DisposeFailedPendingConnectionAsync(IProtocolConnection connection, Task connectTask)
    {
        try
        {
            await connectTask.ConfigureAwait(false);
            Debug.Fail("DisposeFailedPendingConnectionAsync requires a failed or failing connectTask.");
        }
        catch
        {
            // Observe expected exception.
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

    /// <summary>Gets an active connection, by creating and connecting (if necessary) a new protocol connection.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token of the invocation calling this method.</param>
    /// <returns>A connected connection.</returns>
    // TODO: rename corresponding ConnectionCache method currently named ConnectAsync.
    private Task<IProtocolConnection> GetActiveConnectionAsync(CancellationToken cancellationToken)
    {
        (IProtocolConnection Connection, Task<TransportConnectionInformation> ConnectTask) pendingConnectionValue;

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new IceRpcException(IceRpcError.OperationAborted, "The client connection was disposed.");
            }
            if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, "The client connection was shut down.");
            }

            if (_activeConnection is not null)
            {
                return Task.FromResult(_activeConnection.Value.Connection);
            }

            if (_pendingConnection is null)
            {
                IProtocolConnection connection = _connectionFactory();

                // We pass CancellationToken.None because the invocation cancellation should not cancel the connection
                // establishment.
                Task<TransportConnectionInformation> connectTask =
                    CreateConnectTask(connection, _disposedCts.Token, CancellationToken.None);
                _pendingConnection = (connection, connectTask);
            }
            pendingConnectionValue = _pendingConnection.Value;
        }

        return PerformGetActiveConnectionAsync();

        async Task<IProtocolConnection> PerformGetActiveConnectionAsync()
        {
            // ConnectTask itself takes care of scheduling its exception observation when it fails.
            try
            {
                _ = await pendingConnectionValue.ConnectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Canceled via the cancellation token given to ConnectAsync.
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "The connection establishment was canceled by another concurrent attempt.");
            }
            return pendingConnectionValue.Connection;
        }
    }
}
