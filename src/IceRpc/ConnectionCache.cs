// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
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

    private int _backgroundConnectionDisposeCount;

    private readonly TaskCompletionSource _backgroundConnectionDisposeTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _backgroundConnectionShutdownCount;

    private readonly TaskCompletionSource _backgroundConnectionShutdownTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IClientProtocolConnectionFactory _connectionFactory;

    private readonly TimeSpan _connectTimeout;

    // A cancellation token source that is canceled when DisposeAsync is called.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;

    private bool _isShutdown;

    private readonly object _mutex = new();

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<ServerAddress, (IProtocolConnection Connection, Task Task)> _pendingConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly bool _preferExistingConnection;

    private readonly TimeSpan _shutdownTimeout;

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="options">The connection cache options.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol connections.
    /// </param>
    /// <param name="logger">The logger.</param>
    public ConnectionCache(
        ConnectionCacheOptions options,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        ILogger? logger = null)
    {
        _connectionFactory = new ClientProtocolConnectionFactory(
            options.ConnectionOptions,
            options.ClientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport,
            logger);

        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;

        _preferExistingConnection = options.PreferExistingConnection;
    }

    /// <summary>Constructs a connection cache using the default options.</summary>
    public ConnectionCache()
        : this(new ConnectionCacheOptions())
    {
    }

    /// <summary>Releases all resources allocated by this cache. The cache disposes all the the connections it created.
    /// </summary>
    /// <returns>A value task that completes when the disposal of all connections created by this cache has completed.
    /// This includes connections that were active when this method is called and connections whose disposal was
    /// initiated prior to this call.</returns>
    /// <remarks>The disposal of a connection waits for the completion of all dispatch tasks created by this connection.
    /// If the configured dispatcher does not complete promptly when its cancellation token is canceled, the disposal of
    /// a connection and indirectly of the connection cache as a whole can hang.</remarks>
    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_disposeTask is null)
            {
                _disposeTask = PerformDisposeAsync();

                // Once _disposeTask is not null, we no longer perform any background dispose or shutdown.
                if (_backgroundConnectionDisposeCount == 0)
                {
                    // There is no outstanding background dispose.
                    _ = _backgroundConnectionDisposeTcs.TrySetResult();
                }
                // _backgroundConnectionShutdown is only relevant to ShutdownAsync, which can't be called after
                // DisposeAsync.
            }
            return new(_disposeTask);
        }

        async Task PerformDisposeAsync()
        {
            await Task.Yield(); // exit mutex lock

            _disposedCts.Cancel();

            IEnumerable<IProtocolConnection> allConnections =
                _pendingConnections.Values.Select(value => value.Connection).Concat(_activeConnections.Values);

            await Task.WhenAll(allConnections.Select(connection => connection.DisposeAsync().AsTask()))
                .ConfigureAwait(false);

            await _backgroundConnectionDisposeTcs.Task.ConfigureAwait(false);

            _disposedCts.Dispose();
        }
    }

    /// <summary>Sends an outgoing request and returns the corresponding incoming response. If the request
    /// <see cref="IServerAddressFeature" /> feature is not set, the cache sets it from the server addresses of the
    /// target service. It then looks for an active connection.
    /// The <see cref="ConnectionCacheOptions.PreferExistingConnection" /> property influences how the cache selects
    /// this active connection. If no active connection can be found, the cache creates a new connection to one of the
    /// the request's server addresses from the <see cref="IServerAddressFeature" /> feature. If the connection
    /// establishment to <see cref="IServerAddressFeature.ServerAddress" /> is unsuccessful, the cache will try to
    /// establish a connection to one of the <see cref="IServerAddressFeature.AltServerAddresses" /> addresses. Each
    /// connection attempt rotates the server addresses of the server address feature, the main server address corresponding to the last attempt
    /// failure is appended at the end of <see cref="IServerAddressFeature.AltServerAddresses" /> and the first address
    /// from <see cref="IServerAddressFeature.AltServerAddresses" /> replaces
    /// <see cref="IServerAddressFeature.ServerAddress" />. If the cache cannot find an active connection and all
    /// the attempts to establish a new connection fail, this method throws the exception from the last attempt.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        if (request.Features.Get<IServerAddressFeature>() is IServerAddressFeature serverAddressFeature)
        {
            if (serverAddressFeature.ServerAddress is null)
            {
                throw new IceRpcException(
                    IceRpcError.NoConnection,
                    $"Could not invoke '{request.Operation}' on '{request.ServiceAddress}': tried all server addresses without success.");
            }
        }
        else
        {
            if (request.ServiceAddress.ServerAddress is null)
            {
                throw new IceRpcException(
                    IceRpcError.NoConnection,
                    "Cannot send a request to a service without a server address.");
            }

            serverAddressFeature = new ServerAddressFeature(request.ServiceAddress);
            request.Features = request.Features.With(serverAddressFeature);
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            ServerAddress mainServerAddress = serverAddressFeature.ServerAddress!.Value;

            // We retry forever when InvokeAsync (or ConnectAsync) throws an IceRpcException(NoConnection). This
            // exception is usually thrown synchronously by InvokeAsync, however this throwing can be asynchronous when
            // we first connect the connection.
            while (true)
            {
                try
                {
                    IProtocolConnection? connection = null;

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
                                        // This altServerAddress becomes the main server address, and the existing main
                                        // server address becomes the first alt server address.
                                        serverAddressFeature.AltServerAddresses =
                                            serverAddressFeature.AltServerAddresses
                                                .RemoveAt(i)
                                                .Insert(0, mainServerAddress);
                                        serverAddressFeature.ServerAddress = altServerAddress;

                                        break; // for
                                    }
                                }
                            }
                        }

                        IProtocolConnection? GetActiveConnection(ServerAddress serverAddress) =>
                            _activeConnections.TryGetValue(serverAddress, out IProtocolConnection? connection) ?
                                connection : null;
                    }

                    if (connection is null)
                    {
                        try
                        {
                            // TODO: this code generates a UTE
                            connection = await ConnectAsync(mainServerAddress).WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception) when (serverAddressFeature.AltServerAddresses.Count > 0)
                        {
                            // TODO: rework this code. We should not keep going on any exception.
                            for (int i = 0; i < serverAddressFeature.AltServerAddresses.Count; ++i)
                            {
                                // Rotate the server addresses before each new connection attempt: the first alt server
                                // address becomes the main server address and the main server address becomes the last
                                // alt server address.
                                serverAddressFeature.ServerAddress = serverAddressFeature.AltServerAddresses[0];
                                serverAddressFeature.AltServerAddresses =
                                    serverAddressFeature.AltServerAddresses.RemoveAt(0).Add(mainServerAddress);
                                mainServerAddress = serverAddressFeature.ServerAddress.Value;

                                try
                                {
                                    connection = await ConnectAsync(mainServerAddress).WaitAsync(cancellationToken)
                                        .ConfigureAwait(false);
                                    break; // for
                                }
                                catch (Exception) when (i + 1 < serverAddressFeature.AltServerAddresses.Count)
                                {
                                    // and keep trying unless this is the last alt server address.
                                }
                            }
                            Debug.Assert(connection is not null);
                        }
                    }

                    return await connection.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // This can occasionally happen if we find a connection that was just closed and then automatically
                    // disposed by this connection cache.
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.InvocationRefused)
                {
                    // The connection is refusing new invocations.
                }
            }
        }
    }

    /// <summary>Gracefully shuts down all connections created by this cache.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes successfully once the shutdown of all connections created by this cache has
    /// completed. This includes connections that were active when this method is called and connections whose shutdown
    /// was initiated prior to this call. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" /> with error <see cref="IceRpcError.OperationAborted" /> if the
    /// connection cache is disposed while being shut down.</description></item>
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
                throw new ObjectDisposedException($"{typeof(ConnectionCache)}");
            }
            if (_isShutdown)
            {
                throw new InvalidOperationException($"The connection cache is already shut down or shutting down.");
            }
            _isShutdown = true;

            // Once _isShutdown is true, we no longer perform any background dispose or shutdown.
            if (_backgroundConnectionShutdownCount == 0)
            {
                // There is no outstanding background connection shutdown.
                _ = _backgroundConnectionShutdownTcs.TrySetResult();
            }
            // _backgroundConnectionDispose is only relevant to DisposeAsync

            disposedCancellationToken = _disposedCts.Token;
        }

        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            IEnumerable<IProtocolConnection> allConnections =
                _pendingConnections.Values.Select(value => value.Connection).Concat(_activeConnections.Values);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                disposedCancellationToken);

            cts.CancelAfter(_shutdownTimeout);

            try
            {
                await Task.WhenAll(
                    allConnections
                        .Select(connection => connection.ShutdownAsync(cts.Token))
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
                        "The shutdown was aborted because the connection cache was disposed.");
                }
                else
                {
                    throw new TimeoutException(
                        $"The connection cache shut down timed out after {_shutdownTimeout.TotalSeconds} s.");
                }
            }
            catch
            {
                // Ignore connection shutdown failures
            }
        }
    }

    /// <summary>Creates a connection and attempts to connect this connection unless there is an active or pending
    /// connection for the desired server address.</summary>
    /// <param name="serverAddress">The server address.</param>
    /// <returns>A connected connection.</returns>
    private async Task<IProtocolConnection> ConnectAsync(ServerAddress serverAddress)
    {
        (IProtocolConnection Connection, Task Task) pendingConnectionValue;

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ConnectionCache)}");
            }
            if (_isShutdown)
            {
                throw new InvalidOperationException("Connection cache is shut down or shutting down.");
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
                pendingConnectionValue = (connection, PerformConnectAsync(connection, _disposedCts.Token));
                _pendingConnections.Add(serverAddress, pendingConnectionValue);
            }
        }

        await pendingConnectionValue.Task.ConfigureAwait(false);
        return pendingConnectionValue.Connection;

        async Task PerformConnectAsync(IProtocolConnection connection, CancellationToken disposedCancellationToken)
        {
            await Task.Yield(); // exit mutex lock

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(disposedCancellationToken);
            cts.CancelAfter(_connectTimeout);

            try
            {
                try
                {
                    _ = await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!disposedCancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"The connection establishment timed out after {_connectTimeout.TotalSeconds} s.");
                }
            }
            catch
            {
                lock (_mutex)
                {
                    if (_disposeTask is not null)
                    {
                        // The ConnectionCache disposal canceled the connection establishment.
                        throw new IceRpcException(IceRpcError.OperationAborted, "The connection cache was disposed.");
                    }
                    else if (!_isShutdown)
                    {
                        bool removed = _pendingConnections.Remove(serverAddress);
                        Debug.Assert(removed);
                    }
                }

                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            lock (_mutex)
            {
                if (_isShutdown || _disposeTask is not null)
                {
                    // ConnectionCache.DisposeAsync will DisposeAsync this connection.
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection cache was shut down or disposed.");
                }
                else
                {
                    // "move" from pending to active
                    bool removed = _pendingConnections.Remove(serverAddress);
                    Debug.Assert(removed);
                    _activeConnections.Add(serverAddress, connection);
                }
            }
            _ = RemoveFromActiveAsync(connection, disposedCancellationToken);
        }

        async Task RemoveFromActiveAsync(IProtocolConnection connection, CancellationToken disposedCancellationToken)
        {
            bool shutdownRequested =
                await Task.WhenAny(connection.ShutdownRequested, connection.Closed).ConfigureAwait(false) ==
                    connection.ShutdownRequested;

            lock (_mutex)
            {
                if (_isShutdown || _disposeTask is not null)
                {
                    // ConnectionCache.DisposeAsync is responsible to dispose this connection.
                    return;
                }
                else
                {
                    bool removed = _activeConnections.Remove(connection.ServerAddress);
                    Debug.Assert(removed);
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
