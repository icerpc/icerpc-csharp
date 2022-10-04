// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A connection cache is an invoker that routes outgoing requests to connections it manages. This routing is
/// based on the <see cref="IServerAddressFeature" /> and the server addresses of the service address carried by each
/// outgoing request. The connection cache keeps at most one active connection per server address.</summary>
public sealed class ConnectionCache : IInvoker, IAsyncDisposable
{
    // Connected connections that can be returned immediately.
    private readonly Dictionary<ServerAddress, IProtocolConnection> _activeConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly IClientProtocolConnectionFactory _connectionFactory;

    private readonly object _mutex = new();

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<ServerAddress, (IProtocolConnection Connection, Task Task)> _pendingConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly bool _preferExistingConnection;

    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="options">The connection cache options.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol
    /// connections.</param>
    public ConnectionCache(
        ConnectionCacheOptions options,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null)
    {
        _connectionFactory = new LogClientProtocolConnectionFactoryDecorator(
            new ClientProtocolConnectionFactory(
                options.ConnectionOptions,
                options.ClientAuthenticationOptions,
                duplexClientTransport,
                multiplexedClientTransport));

        _preferExistingConnection = options.PreferExistingConnection;
    }

    /// <summary>Constructs a connection cache using the default options.</summary>
    public ConnectionCache()
        : this(new ConnectionCacheOptions())
    {
    }

    /// <summary>Releases all resources allocated by this connection cache.</summary>
    /// <returns>A value task that completes when all connections managed by this cache are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            // We always cancel _shutdownCts with _mutex locked. This way, when _mutex is locked, _shutdownCts.Token
            // does not change.
            try
            {
                _shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed by a previous or concurrent call.
            }
        }

        // Dispose all connections managed by this cache.
        IEnumerable<IProtocolConnection> allConnections = _pendingConnections.Values.Select(value => value.Connection)
            .Concat(_activeConnections.Values);

        await Task.WhenAll(allConnections.Select(connection => connection.DisposeAsync().AsTask()))
            .ConfigureAwait(false);

        _shutdownCts.Dispose();
    }

    /// <summary>Sends an outgoing request and returns the corresponding incoming response. If the request
    /// <see cref="IServerAddressFeature" /> feature is not set, the cache sets it from the server addresses of the
    /// target service. It then looks for an active connection.
    /// The <see cref="ConnectionCacheOptions.PreferExistingConnection" /> property influences how the cache selects
    /// this active connection. If no active connection can be found, the cache creates a new connection to one of the
    /// server address from the request <see cref="IServerAddressFeature" /> feature. If the connection establishment to
    /// <see cref="IServerAddressFeature.ServerAddress" /> is unsuccessful, the cache will try to establish a connection
    /// to one of the <see cref="IServerAddressFeature.AltServerAddresses" /> addresses. Each connection attempt rotates
    /// the server addresses of the server address feature, the main server address corresponding to the last attempt
    /// failure is appended at the end of <see cref="IServerAddressFeature.AltServerAddresses" /> and the first address
    /// from <see cref="IServerAddressFeature.AltServerAddresses" /> replaces
    /// <see cref="IServerAddressFeature.ServerAddress" />.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        if (request.Features.Get<IServerAddressFeature>() is not IServerAddressFeature serverAddressFeature)
        {
            serverAddressFeature = new ServerAddressFeature(request.ServiceAddress);
            request.Features = request.Features.With(serverAddressFeature);
        }

        if (serverAddressFeature.ServerAddress is null)
        {
            throw new NoServerAddressException(request.ServiceAddress);
        }

        IProtocolConnection? connection = null;
        ServerAddress mainServerAddress = serverAddressFeature.ServerAddress!.Value;

        if (_preferExistingConnection)
        {
            lock (_mutex)
            {
                connection = GetActiveConnection(mainServerAddress);
                if (connection is null)
                {
                    for (int i = 0; i < serverAddressFeature.AltServerAddresses.Count; ++i)
                    {
                        ServerAddress altServerAddress = serverAddressFeature.AltServerAddresses[i];
                        connection = GetActiveConnection(altServerAddress);
                        if (connection is not null)
                        {
                            // This altServerAddress becomes the main server address, and the existing main server
                            // address becomes the first alt server address.
                            serverAddressFeature.AltServerAddresses = serverAddressFeature.AltServerAddresses
                                .RemoveAt(i)
                                .Insert(0, mainServerAddress);
                            serverAddressFeature.ServerAddress = altServerAddress;

                            break; // foreach
                        }
                    }
                }
            }

            IProtocolConnection? GetActiveConnection(ServerAddress serverAddress) =>
                _activeConnections.TryGetValue(serverAddress, out IProtocolConnection? connection) ? connection : null;
        }

        if (connection is not null)
        {
            try
            {
                return connection.InvokeAsync(request, cancellationToken);
            }
            catch (ObjectDisposedException exception) when (
                exception.InnerException is ConnectionException connectionException &&
                connectionException.ErrorCode.IsClosedErrorCode())
            {
                // This can occasionally happen if we find a connection that was just closed  and then automatically
                // disposed by this connection cache.
                // TODO: Should we retry? https://github.com/zeroc-ice/icerpc-csharp/issues/1724#issuecomment-1235609102
                throw ExceptionUtil.Throw(connectionException);
            }
        }
        else
        {
            return PerformInvokeAsync();
        }

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            try
            {
                connection = await ConnectAsync(mainServerAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                List<Exception>? exceptionList = null;

                for (int i = 0; i < serverAddressFeature.AltServerAddresses.Count; ++i)
                {
                    // Rotate the server addresses before each new connection attempt: the first alt server address
                    // becomes the main server address and the main server address becomes the last alt server address.
                    serverAddressFeature.ServerAddress = serverAddressFeature.AltServerAddresses[0];
                    serverAddressFeature.AltServerAddresses =
                        serverAddressFeature.AltServerAddresses.RemoveAt(0).Add(mainServerAddress);
                    mainServerAddress = serverAddressFeature.ServerAddress.Value;

                    try
                    {
                        connection = await ConnectAsync(mainServerAddress, cancellationToken).ConfigureAwait(false);
                        break; // for
                    }
                    catch (Exception altEx)
                    {
                        exceptionList ??= new List<Exception> { exception };
                        exceptionList.Add(altEx);
                        // and keep trying
                    }
                }

                if (connection is null)
                {
                    if (exceptionList is null)
                    {
                        throw;
                    }
                    else
                    {
                        throw new AggregateException(exceptionList);
                    }
                }
            }

            try
            {
                return await connection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException exception) when (
                exception.InnerException is ConnectionException connectionException &&
                connectionException.ErrorCode.IsClosedErrorCode())
            {
                // This can occasionally happen if we find a connection that was just closed and then automatically
                // disposed by this connection cache.
                // TODO: Should we retry? https://github.com/zeroc-ice/icerpc-csharp/issues/1724#issuecomment-1235609102
                throw ExceptionUtil.Throw(connectionException);
            }
        }
    }

    /// <summary>Gracefully shuts down all connections managed by this cache, and send a default message to the servers.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        ShutdownAsync("ConnectionCache shutdown", cancellationToken);

    /// <summary>Gracefully shuts down all connections managed by this cache.</summary>
    /// <param name="message">The message to send to the server with the icerpc protocol.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the shutdown is complete.</returns>
    public Task ShutdownAsync(string message, CancellationToken cancellationToken = default)
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
                throw new ObjectDisposedException($"{typeof(ConnectionCache)}");
            }
        }

        // Shut down all connections managed by this cache.
        IEnumerable<IProtocolConnection> allConnections = _pendingConnections.Values.Select(value => value.Connection)
            .Concat(_activeConnections.Values);

        return Task.WhenAll(
            allConnections.Select(connection => connection.ShutdownAsync(message, cancellationToken)));
    }

    /// <summary>Creates a connection and attempts to connect this connection unless there is an active or pending
    /// connection for the desired server address.</summary>
    /// <param name="serverAddress">The server address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A connected connection.</returns>
    private async ValueTask<IProtocolConnection> ConnectAsync(
        ServerAddress serverAddress,
        CancellationToken cancellationToken)
    {
        (IProtocolConnection Connection, Task Task) pendingConnectionValue;

        CancellationToken shutdownCancellationToken;

        lock (_mutex)
        {
            try
            {
                shutdownCancellationToken = _shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException($"{typeof(ConnectionCache)}");
            }

            if (shutdownCancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("connection cache is shut down or shutting down");
            }

            if (_activeConnections.TryGetValue(serverAddress, out IProtocolConnection? connection))
            {
                return connection;
            }
            else if (_pendingConnections.TryGetValue(serverAddress, out pendingConnectionValue))
            {
                // and wait for the task to complete outside the mutex lock
            }
            else
            {
                connection = _connectionFactory.CreateConnection(serverAddress);
                pendingConnectionValue = (connection, PerformConnectAsync(connection));
                _pendingConnections.Add(serverAddress, pendingConnectionValue);
            }
        }

        await pendingConnectionValue.Task.ConfigureAwait(false);
        return pendingConnectionValue.Connection;

        async Task PerformConnectAsync(IProtocolConnection connection)
        {
            await Task.Yield(); // exit mutex lock

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                shutdownCancellationToken);
            try
            {
                _ = await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                lock (_mutex)
                {
                    // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                    if (shutdownCancellationToken.IsCancellationRequested)
                    {
                        // The ConnectionCache shut down or disposal canceled the connection establishment.
                        // ConnectionCache.DisposeAsync will DisposeAsync this connection.
                        throw new ConnectionException(ConnectionErrorCode.ClosedByShutdown);
                    }
                    else
                    {
                        bool removed = _pendingConnections.Remove(serverAddress);
                        Debug.Assert(removed);
                    }
                }

                await connection.DisposeAsync().ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested(); // throws OCE
                throw;
            }

            lock (_mutex)
            {
                // shutdownCancellationToken.IsCancellationRequested remains the same when _mutex is locked.
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // ConnectionCache.DisposeAsync will DisposeAsync this connection.
                    throw new ConnectionException(ConnectionErrorCode.ClosedByShutdown);
                }
                else
                {
                    // "move" from pending to active
                    bool removed = _pendingConnections.Remove(serverAddress);
                    Debug.Assert(removed);
                    _activeConnections.Add(serverAddress, connection);
                }
            }
            _ = RemoveFromActiveAsync(connection, shutdownCancellationToken);
        }

        async Task RemoveFromActiveAsync(IProtocolConnection connection, CancellationToken shutdownCancellationToken)
        {
            try
            {
                _ = await connection.ShutdownComplete.WaitAsync(shutdownCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == shutdownCancellationToken)
            {
                // The connection cache is being shut down or disposed and cache's DisposeAsync is responsible to
                // DisposeAsync this connection.
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
                    // ConnectionCache.DisposeAsync is responsible to dispose this connection.
                    return;
                }
                else
                {
                    bool removed = _activeConnections.Remove(connection.ServerAddress);
                    Debug.Assert(removed);
                }
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
