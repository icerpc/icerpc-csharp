// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

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

    private readonly TimeSpan _connectTimeout;

    // A detached connection is a protocol connection that is shutting down or being disposed. Both
    // ConnectionCache.ShutdownAsync and DisposeAsync wait for detached connections to reach 0 using
    // _detachedConnectionsTcs. Such a connection is "detached" because it's not in _activeConnections or
    // _pendingConnections.
    private int _detachedConnectionCount;

    private readonly TaskCompletionSource _detachedConnectionsTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A cancellation token source that is canceled when DisposeAsync is called.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;

    private readonly object _mutex = new();

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<ServerAddress, (IProtocolConnection Connection, Task Task)> _pendingConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly bool _preferExistingConnection;

    private Task? _shutdownTask;

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

    /// <summary>Releases all resources allocated by this cache. The cache disposes all the connections it created.
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
                _shutdownTask ??= Task.CompletedTask;
                if (_detachedConnectionCount == 0)
                {
                    _ = _detachedConnectionsTcs.TrySetResult();
                }

                _disposeTask = PerformDisposeAsync();
            }
            return new(_disposeTask);
        }

        async Task PerformDisposeAsync()
        {
            await Task.Yield(); // exit mutex lock

            _disposedCts.Cancel();

            IEnumerable<IProtocolConnection> allConnections =
                _pendingConnections.Values.Select(value => value.Connection).Concat(_activeConnections.Values);

            try
            {
                await Task.WhenAll(
                    allConnections.Select(connection => connection.DisposeAsync().AsTask())
                        .Append(_shutdownTask)
                        .Append(_detachedConnectionsTcs.Task)).ConfigureAwait(false);
            }
            catch
            {
                // Ignore _shutdownTask failure or cancellation.
            }

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
    /// connection attempt rotates the server addresses of the server address feature, the main server address
    /// corresponding to the last attempt failure is appended at the end of
    /// <see cref="IServerAddressFeature.AltServerAddresses" /> and the first address from
    /// <see cref="IServerAddressFeature.AltServerAddresses" /> replaces
    /// <see cref="IServerAddressFeature.ServerAddress" />. If the cache cannot find an active connection and all
    /// the attempts to establish a new connection fail, this method throws the exception from the last attempt.
    /// </summary>
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

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ConnectionCache)}");
            }
            if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, "The connection cache is shut down.");
            }
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            ServerAddress mainServerAddress = serverAddressFeature.ServerAddress!.Value;

            // When InvokeAsync (or ConnectAsync) throws an IceRpcException(InvocationRefused) we retry unless the
            // cache is being shutdown. This exception is usually thrown synchronously by InvokeAsync, however this
            // throwing can be asynchronous when we first connect the connection.
            while (true)
            {
                IProtocolConnection? connection = null;

                if (_preferExistingConnection)
                {
                    lock (_mutex)
                    {
                        var enumerator = new ServerAddressEnumerator(serverAddressFeature);
                        while (enumerator.MoveNext())
                        {
                            ServerAddress serverAddress = enumerator.Current;
                            if (_activeConnections.TryGetValue(serverAddress, out connection))
                            {
                                if (enumerator.CurrentIndex > 0)
                                {
                                    // This altServerAddress becomes the main server address, and the existing main
                                    // server address becomes the first alt server address.
                                    serverAddressFeature.AltServerAddresses =
                                        serverAddressFeature.AltServerAddresses
                                            .RemoveAt(enumerator.CurrentIndex - 1)
                                            .Insert(0, mainServerAddress);
                                    serverAddressFeature.ServerAddress = serverAddress;
                                }
                                break; // for
                            }
                        }
                    }
                }

                if (connection is null)
                {
                    Exception? connectionException = null;
                    var enumerator = new ServerAddressEnumerator(serverAddressFeature);
                    while (enumerator.MoveNext())
                    {
                        if (enumerator.CurrentIndex > 0)
                        {
                            // Rotate the server addresses before each new connection attempt after the initial attempt:
                            // the first alt server address becomes the main server address and the main server address
                            // becomes the last alt server address.
                            serverAddressFeature.ServerAddress = serverAddressFeature.AltServerAddresses[0];
                            serverAddressFeature.AltServerAddresses =
                                serverAddressFeature.AltServerAddresses.RemoveAt(0).Add(mainServerAddress);
                            mainServerAddress = serverAddressFeature.ServerAddress.Value;
                        }

                        try
                        {
                            connection = await ConnectAsync(mainServerAddress).WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                            break;
                        }
                        catch (TimeoutException exception)
                        {
                            connectionException = exception;
                        }
                        catch (InvalidDataException exception)
                        {
                            connectionException = exception;
                        }
                        // TODO are there other IceRpcError errors besides IceRpcError.IceRpcError that we want to
                        // let through.
                        catch (IceRpcException exception) when (exception.IceRpcError != IceRpcError.IceRpcError)
                        {
                            // keep going unless the connection cache was disposed or shut down
                            connectionException = exception;
                            lock (_mutex)
                            {
                                if (_shutdownTask is not null)
                                {
                                    throw;
                                }
                            }
                        }
                    }

                    if (connection is null)
                    {
                        Debug.Assert(connectionException is not null);
                        ExceptionDispatchInfo.Throw(connectionException);
                        Debug.Assert(false);
                    }
                }

                try
                {
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

                lock (_mutex)
                {
                    if (_disposeTask is not null)
                    {
                        throw new IceRpcException(IceRpcError.OperationAborted, "The connection cache was disposed.");
                    }

                    if (_shutdownTask is not null)
                    {
                        throw new IceRpcException(IceRpcError.InvocationRefused, "The connection cache is shut down.");
                    }
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
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ConnectionCache)}");
            }
            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException("The connection cache is already shut down or shutting down.");
            }

            if (_detachedConnectionCount == 0)
            {
                _detachedConnectionsTcs.SetResult();
            }

            _shutdownTask = PerformShutdownAsync();
        }

        return _shutdownTask;

        async Task PerformShutdownAsync()
        {
            await Task.Yield(); // exit mutex lock

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposedCts.Token);
            cts.CancelAfter(_shutdownTimeout);

            try
            {
                try
                {
                    await Task.WhenAll(
                        _pendingConnections.Values.Select(
                            value => ShutdownPendingAsync(value.Connection, value.Task, cts.Token))
                        .Concat(_activeConnections.Values.Select(connection => connection.ShutdownAsync(cts.Token)))
                        .Append(_detachedConnectionsTcs.Task.WaitAsync(cts.Token))).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Ignore other connection shutdown failures.

                    // Throw OperationCanceledException if this WhenAll exception is hiding an OCE.
                    cts.Token.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_disposedCts.IsCancellationRequested)
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
        }

        // For pending connections, we need to wait for the _connectTask to complete successfully before calling
        // ShutdownAsync.
        static async Task ShutdownPendingAsync(
            IProtocolConnection connection,
            Task connectTask,
            CancellationToken cancellationToken)
        {
            // First wait for the ConnectAsync to complete
            try
            {
                await connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch
            {
                // connectTask failed = successful shutdown
                return;
            }

            await connection.ShutdownAsync(cancellationToken).ConfigureAwait(false);
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
                throw new IceRpcException(IceRpcError.OperationAborted, "The connection cache was disposed.");
            }
            else if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, "The connection cache is shut down.");
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

        async Task PerformConnectAsync(IProtocolConnection connection, CancellationToken cancellationToken)
        {
            await Task.Yield(); // exit mutex lock

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_connectTimeout);

            try
            {
                try
                {
                    _ = await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
                    else if (_shutdownTask is null)
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
                if (_disposeTask is not null)
                {
                    // ConnectionCache.DisposeAsync will DisposeAsync this connection.
                    throw new IceRpcException(IceRpcError.OperationAborted, "The connection cache was disposed.");
                }
                else if (_shutdownTask is not null)
                {
                    throw new IceRpcException(IceRpcError.InvocationRefused, "The connection cache is shut down.");
                }

                // "move" from pending to active
                bool removed = _pendingConnections.Remove(serverAddress);
                Debug.Assert(removed);
                _activeConnections.Add(serverAddress, connection);
            }
            _ = RemoveFromActiveAsync(connection, cancellationToken);
        }

        async Task RemoveFromActiveAsync(IProtocolConnection connection, CancellationToken disposedCancellationToken)
        {
            bool shutdownRequested =
                await Task.WhenAny(connection.ShutdownRequested, connection.Closed).ConfigureAwait(false) ==
                    connection.ShutdownRequested;

            lock (_mutex)
            {
                if (_shutdownTask is null)
                {
                    bool removed = _activeConnections.Remove(connection.ServerAddress);
                    Debug.Assert(removed);
                    _detachedConnectionCount++;
                }
                else
                {
                    // ConnectionCache.DisposeAsync is responsible to dispose this connection.
                    return;
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
                catch
                {
                    // Ignore connection shutdown failures
                }
            }

            await connection.DisposeAsync().ConfigureAwait(false);

            lock (_mutex)
            {
                if (--_detachedConnectionCount == 0 && _disposeTask is not null)
                {
                    _detachedConnectionsTcs.SetResult();
                }
            }
        }
    }

    // A helper struct that implements and enumerator that allows iterating the addresses of a IServerAddressFeature
    // without allocations.
    private struct ServerAddressEnumerator
    {
        internal ServerAddress Current
        {
            get
            {
                Debug.Assert(CurrentIndex >= 0 && CurrentIndex <= _altServerAddresses.Count);
                if (CurrentIndex == 0)
                {
                    Debug.Assert(_mainServerAddress is not null);
                    return _mainServerAddress.Value;
                }
                else
                {
                    return _altServerAddresses[CurrentIndex - 1];
                }
            }
        }

        internal int Count { get; }

        internal int CurrentIndex { get; private set; } = -1;

        private readonly ServerAddress? _mainServerAddress;
        private readonly IList<ServerAddress> _altServerAddresses;

        internal bool MoveNext()
        {
            if (CurrentIndex == -1)
            {
                if (_mainServerAddress is not null)
                {
                    CurrentIndex++;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if (CurrentIndex < _altServerAddresses.Count)
            {
                CurrentIndex++;
                return true;
            }
            return false;
        }

        internal ServerAddressEnumerator(IServerAddressFeature serverAddressFeature)
        {
            _mainServerAddress = serverAddressFeature.ServerAddress;
            _altServerAddresses = serverAddressFeature.AltServerAddresses;
            if (_mainServerAddress is null)
            {
                Count = 0;
            }
            else
            {
                Count = _altServerAddresses.Count + 1;
            }
        }
    }
}
