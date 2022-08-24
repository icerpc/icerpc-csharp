// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A connection cache is an invoker that routes outgoing requests to connections it manages. This routing is
/// based on the <see cref="IServerAddressFeature"/> and the server addresses of the service address carried by each
/// outgoing request.</summary>
public sealed class ConnectionCache : IInvoker, IAsyncDisposable
{
    // Connected connections that can be returned immediately.
    private readonly Dictionary<ServerAddress, IProtocolConnection> _activeConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly IClientProtocolConnectionFactory _connectionFactory;

    private readonly object _mutex = new();

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<ServerAddress, IProtocolConnection> _pendingConnections =
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
            // We always cancel _shutdownCts with _mutex lock. This way, when _mutex is locked, _shutdownCts.Token
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
        IEnumerable<IProtocolConnection> allConnections = _pendingConnections.Values.Concat(_activeConnections.Values);

        await Task.WhenAll(allConnections.Select(connection => connection.DisposeAsync().AsTask()))
            .ConfigureAwait(false);

        _shutdownCts.Dispose();
    }

    /// <inheritdoc/>
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
            catch (ObjectDisposedException)
            {
                throw new ConnectionClosedException();
            }
        }
    }

    /// <summary>Gracefully shuts down all connections managed by this cache. This method can be called multiple times.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
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
                // already disposed
                return Task.CompletedTask;
            }
        }

        // Shut down all connections managed by this cache.
        IEnumerable<IProtocolConnection> allConnections = _pendingConnections.Values.Concat(_activeConnections.Values);

        return Task.WhenAll(
            allConnections.Select(connection => connection.ShutdownAsync("connection cache shutdown", cancellationToken)));
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
        IProtocolConnection? connection = null;
        bool created = false;

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
                connection = _connectionFactory.CreateConnection(serverAddress);
                created = true;
                _pendingConnections.Add(serverAddress, connection);
            }
        }

        if (created)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                shutdownCancellationToken);
            try
            {
                TransportConnectionInformation transportConnectionInformation =
                    await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                lock (_mutex)
                {
                    if (shutdownCancellationToken.IsCancellationRequested)
                    {
                        // ConnectionCache is being shut down or disposed and ConnectionCache.DisposeAsync will
                        // DisposeAsync this connection.
                        throw new ConnectionClosedException();
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
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // ConnectionCache is being shut down or disposed and ConnectionCache.DisposeAsync will
                    // DisposeAsync this connection.
                    throw new ConnectionClosedException();
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
        else
        {
            _ = await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;

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

            bool disposeConnection = true;

            lock (_mutex)
            {
                if (shutdownCancellationToken.IsCancellationRequested)
                {
                    // ConnectionCache.DisposeAsync is responsible to dispose this connection.
                    disposeConnection = true;
                }
                else
                {
                    bool removed = _activeConnections.Remove(connection.ServerAddress);
                    Debug.Assert(removed);
                }
            }

            if (disposeConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Provides a log decorator for protocol connection factory.</summary>
    private class LogClientProtocolConnectionFactoryDecorator : IClientProtocolConnectionFactory
    {
        private readonly IClientProtocolConnectionFactory _decoratee;

        public IProtocolConnection CreateConnection(ServerAddress serverAddress)
        {
            IProtocolConnection connection = _decoratee.CreateConnection(serverAddress);
            ConnectionCacheEventSource.Log.ConnectionStart(serverAddress);
            return new LogProtocolConnectionDecorator(connection);
        }

        internal LogClientProtocolConnectionFactoryDecorator(IClientProtocolConnectionFactory decoratee) =>
            _decoratee = decoratee;
    }

    /// <summary>Provides a log decorator for protocol connection.</summary>
    private class LogProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task<string> ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly IProtocolConnection _decoratee;

        private readonly Task _logShutdownAsync;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectionCacheEventSource.Log.ConnectStart(ServerAddress);
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                ConnectionCacheEventSource.Log.ConnectSuccess(ServerAddress, result.LocalNetworkAddress!);
                return result;
            }
            catch (Exception exception)
            {
                ConnectionCacheEventSource.Log.ConnectFailure(ServerAddress, exception);
                throw;
            }
            finally
            {
                ConnectionCacheEventSource.Log.ConnectStop(ServerAddress);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            await _logShutdownAsync.ConfigureAwait(false); // make sure the task completes before ConnectionStop
            ConnectionCacheEventSource.Log.ConnectionStop(ServerAddress);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public void OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

        public void OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

        public Task ShutdownAsync(string message, CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(message, cancellationToken);

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee)
        {
            _decoratee = decoratee;
            _logShutdownAsync = LogShutdownAsync();

            async Task LogShutdownAsync()
            {
                try
                {
                    string message = await ShutdownComplete.ConfigureAwait(false);
                    ConnectionCacheEventSource.Log.ConnectionShutdown(ServerAddress, message);
                }
                catch (Exception exception)
                {
                    ConnectionCacheEventSource.Log.ConnectionShutdownFailure(ServerAddress, exception);
                }
            }
        }
    }
}
