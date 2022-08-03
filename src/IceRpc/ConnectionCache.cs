// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A connection cache is an invoker that routes outgoing requests to connections it manages. This routing is
/// based on the <see cref="IServerAddressFeature"/> and the server addresses of the service address carried by each
/// outgoing request.</summary>
public sealed class ConnectionCache : IInvoker, IAsyncDisposable
{
    // Connected connections that can be returned immediately.
    private readonly Dictionary<ServerAddress, ClientConnection> _activeConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly Func<ServerAddress, ClientConnection> _clientConnectionFactory;

    private bool _isReadOnly;

    private readonly object _mutex = new();

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<ServerAddress, ClientConnection> _pendingConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly bool _preferExistingConnection;

    // Formerly pending or active connections that are closed but not shutdown yet.
    private readonly HashSet<ClientConnection> _shutdownPendingConnections = new();

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="options">The connection cache options.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
    /// </param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol
    /// connections.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    public ConnectionCache(
        ConnectionCacheOptions options,
        ILoggerFactory? loggerFactory = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        IDuplexClientTransport? duplexClientTransport = null)
    {
        _preferExistingConnection = options.PreferExistingConnection;

        multiplexedClientTransport ??= ClientConnection.DefaultMultiplexedClientTransport;
        duplexClientTransport ??= ClientConnection.DefaultDuplexClientTransport;

        _clientConnectionFactory = serverAddress => new ClientConnection(
            options.ClientConnectionOptions with { ServerAddress = serverAddress },
            loggerFactory,
            multiplexedClientTransport,
            duplexClientTransport);
    }

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="clientConnectionOptions">The client connection options for connections created by this cache.
    /// </param>
    public ConnectionCache(ClientConnectionOptions clientConnectionOptions)
        : this(new ConnectionCacheOptions { ClientConnectionOptions = clientConnectionOptions })
    {
    }

    /// <summary>Releases all resources allocated by this connection cache.</summary>
    /// <returns>A value task that completes when all connections managed by this cache are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _isReadOnly = true;
        }

        // Dispose all connections managed by this cache.
        IEnumerable<ClientConnection> allConnections =
            _pendingConnections.Values.Concat(_activeConnections.Values).Concat(_shutdownPendingConnections);

        await Task.WhenAll(allConnections.Select(connection => connection.DisposeAsync().AsTask()))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
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

        ClientConnection? connection = null;
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

            ClientConnection? GetActiveConnection(ServerAddress serverAddress) =>
                _activeConnections.TryGetValue(serverAddress, out ClientConnection? connection) ? connection : null;
        }

        if (connection is not null)
        {
            try
            {
                return connection.InvokeAsync(request, cancel);
            }
            catch (ObjectDisposedException)
            {
                // This can occasionally happen if we find a connection that was just closed by the peer or transport
                // and then automatically disposed by this connection cache.
                throw new ConnectionClosedException();
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
                connection = await ConnectAsync(mainServerAddress, cancel).ConfigureAwait(false);
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
                        connection = await ConnectAsync(mainServerAddress, cancel).ConfigureAwait(false);
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
                return await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                throw new ConnectionClosedException();
            }
        }
    }

    /// <summary>Gracefully shuts down all connections managed by this cache. This method can be called multiple times.
    /// </summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            _isReadOnly = true;
        }

        // Shut down all connections managed by this cache.
        IEnumerable<ClientConnection> allConnections =
            _pendingConnections.Values.Concat(_activeConnections.Values).Concat(_shutdownPendingConnections);

        return Task.WhenAll(
            allConnections.Select(connection => connection.ShutdownAsync("connection cache shutdown", cancel)));
    }

    /// <summary>Creates a connection and attempts to connect this connection unless there is an active or pending
    /// connection for the desired server address.</summary>
    /// <param name="serverAddress">The server address.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A connected connection.</returns>
    private async ValueTask<ClientConnection> ConnectAsync(ServerAddress serverAddress, CancellationToken cancel)
    {
        ClientConnection? connection = null;
        bool created = false;

        lock (_mutex)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("connection cache shutting down");
            }

            if (_activeConnections.TryGetValue(serverAddress, out connection))
            {
                return connection;
            }
            else if (_pendingConnections.TryGetValue(serverAddress, out connection))
            {
                // and call ConnectAsync on this connection after the if block.
            }
            else
            {
                connection = _clientConnectionFactory(serverAddress);
                created = true;
                _pendingConnections.Add(serverAddress, connection);
            }
        }

        if (created)
        {
            try
            {
                // TODO: add cancellation token to cancel when ConnectionCache is shut down / disposed.
                await connection.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch
            {
                bool scheduleRemoveFromClosed = false;

                lock (_mutex)
                {
                    // the _pendingConnections collection is read-only after shutdown
                    if (!_isReadOnly)
                    {
                        // "move" from pending to shutdown pending
                        bool removed = _pendingConnections.Remove(serverAddress);
                        Debug.Assert(removed);
                        _ = _shutdownPendingConnections.Add(connection);
                        scheduleRemoveFromClosed = true;
                    }
                }
                if (scheduleRemoveFromClosed)
                {
                    _ = RemoveFromClosedAsync(connection, graceful: false);
                }

                throw;
            }

            bool scheduleRemoveFromActive = false;

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // "move" from pending to active
                    bool removed = _pendingConnections.Remove(serverAddress);
                    Debug.Assert(removed);
                    _activeConnections.Add(serverAddress, connection);
                    scheduleRemoveFromActive = true;
                }
                // this new connection is being shut down already
            }

            if (scheduleRemoveFromActive)
            {
                // Schedule removal after addition. We do this outside the mutex lock otherwise RemoveFromActive could
                // call await ShutdownAsync or DisposeAsync on the connection within this lock.
                connection.OnAbort(exception => RemoveFromActive(graceful: false));
                connection.OnShutdown(message => RemoveFromActive(graceful: true));
            }
        }
        else
        {
            await connection.ConnectAsync(cancel).ConfigureAwait(false);
        }

        return connection;

        void RemoveFromActive(bool graceful)
        {
            Debug.Assert(connection is not null);

            bool scheduleRemoveFromClosed = false;

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // "move" from active to shutdown pending
                    bool removed = _activeConnections.Remove(connection.ServerAddress);
                    Debug.Assert(removed);
                    _ = _shutdownPendingConnections.Add(connection);
                    scheduleRemoveFromClosed = true;
                }
            }

            if (scheduleRemoveFromClosed)
            {
                _ = RemoveFromClosedAsync(connection, graceful);
            }
        }

        // Remove connection from _shutdownPendingConnections once the dispose is complete
        async Task RemoveFromClosedAsync(ClientConnection clientConnection, bool graceful)
        {
            if (graceful)
            {
                // wait for current shutdown to complete
                try
                {
                    await clientConnection.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            await clientConnection.DisposeAsync().ConfigureAwait(false);

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    _ = _shutdownPendingConnections.Remove(clientConnection);
                }
            }
        }
    }
}
