// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace IceRpc;

/// <summary>Represents an invoker that routes outgoing requests to connections it manages. The connection cache keeps
/// at most one active connection per server address.</summary>
/// <remarks>The connection cache routes requests based on the request's <see cref="IServerAddressFeature" /> feature
/// and the server addresses of the request's target service.</remarks>
public sealed class ConnectionCache : IInvoker, IAsyncDisposable
{
    // Connected connections.
    private readonly Dictionary<ServerAddress, IProtocolConnection> _activeConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly IClientProtocolConnectionFactory _connectionFactory;

    private readonly TimeSpan _connectTimeout;

    // A detached connection is a protocol connection that is connecting, shutting down or being disposed. Both
    // ShutdownAsync and DisposeAsync wait for detached connections to reach 0 using _detachedConnectionsTcs. Such a
    // connection is "detached" because it's not in _activeConnections.
    private int _detachedConnectionCount;

    private readonly TaskCompletionSource _detachedConnectionsTcs = new();

    // A cancellation token source that is canceled when DisposeAsync is called.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;

    private readonly object _mutex = new();

    // New connections in the process of connecting.
    private readonly Dictionary<ServerAddress, (IProtocolConnection Connection, Task ConnectTask)> _pendingConnections =
        new(ServerAddressComparer.OptionalTransport);

    private readonly bool _preferExistingConnection;

    private Task? _shutdownTask;

    private readonly TimeSpan _shutdownTimeout;

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="options">The connection cache options.</param>
    /// <param name="duplexClientTransport">The duplex client transport. <see langword="null" /> is equivalent to <see
    /// cref="IDuplexClientTransport.Default" />.</param>
    /// <param name="multiplexedClientTransport">The multiplexed client transport. <see langword="null" /> is equivalent
    /// to <see cref="IMultiplexedClientTransport.Default" />.</param>
    /// <param name="logger">The logger. <see langword="null" /> is equivalent to <see cref="NullLogger.Instance"
    /// />.</param>
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

    /// <summary>Releases all resources allocated by the cache. The cache disposes all the connections it
    /// created.</summary>
    /// <returns>A value task that completes when the disposal of all connections created by this cache has completed.
    /// This includes connections that were active when this method is called and connections whose disposal was
    /// initiated prior to this call.</returns>
    /// <remarks>The disposal of an underlying connection of the cache  aborts invocations, cancels dispatches and
    /// disposes the underlying transport connection without waiting for the peer. To wait for invocations and
    /// dispatches to complete, call <see cref="ShutdownAsync" /> first. If the configured dispatcher does not complete
    /// promptly when its cancellation token is canceled, the disposal can hang.</remarks>
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

            // Wait for shutdown before disposing connections.
            try
            {
                await _shutdownTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions.
            }

            // Since a pending connection is "detached", it's disposed via the connectTask, not directly by this method.
            await Task.WhenAll(
                _activeConnections.Values.Select(connection => connection.DisposeAsync().AsTask())
                    .Append(_detachedConnectionsTcs.Task)).ConfigureAwait(false);

            _disposedCts.Dispose();
        }
    }

    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    /// <remarks><para>If the request <see cref="IServerAddressFeature" /> feature is not set, the cache sets it from
    /// the server addresses of the target service.</para>
    /// <para>It then looks for an active connection. The <see cref="ConnectionCacheOptions.PreferExistingConnection" />
    /// property influences how the cache selects this active connection. If no active connection can be found, the
    /// cache creates a new connection to one
    /// of the request's server addresses from the <see cref="IServerAddressFeature" /> feature.</para>
    /// <para>If the connection establishment to <see cref="IServerAddressFeature.ServerAddress" /> is unsuccessful,
    /// the cache will try to establish a connection to one of the
    /// <see cref="IServerAddressFeature.AltServerAddresses" /> addresses. Each connection attempt rotates the server
    /// addresses of the server address feature, the main server address corresponding to the last attempt failure is
    /// appended at the end of <see cref="IServerAddressFeature.AltServerAddresses" /> and the first address from
    /// <see cref="IServerAddressFeature.AltServerAddresses" /> replaces
    /// <see cref="IServerAddressFeature.ServerAddress" />. If the cache cannot find an active connection and all
    /// the attempts to establish a new connection fail, this method throws the exception from the last attempt.</para>
    /// </remarks>
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
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, "The connection cache was shut down.");
            }
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            Debug.Assert(serverAddressFeature.ServerAddress is not null);

            // When InvokeAsync (or ConnectAsync) throws an IceRpcException(InvocationRefused) we retry unless the
            // cache is being shutdown.
            while (true)
            {
                IProtocolConnection? connection = null;
                if (_preferExistingConnection)
                {
                    _ = TryGetActiveConnection(serverAddressFeature, out connection);
                }
                connection ??= await GetActiveConnectionAsync(serverAddressFeature, cancellationToken)
                    .ConfigureAwait(false);

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
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
                {
                    lock (_mutex)
                    {
                        if (_disposeTask is null)
                        {
                            throw new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                "The underlying connection was disposed while the invocation was in progress.");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                // Make sure connection is no longer in _activeConnection before we retry.
                _ = RemoveFromActiveAsync(serverAddressFeature.ServerAddress.Value, connection);
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
    /// <item><description><see cref="OperationCanceledException" /> if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" /> if the shutdown timed out.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the server is disposed.</exception>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

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
                // Since a pending connection is "detached", it's shutdown and disposed via the connectTask, not
                // directly by this method.
                try
                {
                    await Task.WhenAll(
                        _activeConnections.Values.Select(connection => connection.ShutdownAsync(cts.Token))
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
    }

    private async Task CreateConnectTask(IProtocolConnection connection, ServerAddress serverAddress)
    {
        await Task.Yield(); // exit mutex lock

        // This task "owns" a detachedConnectionCount and as a result _disposedCts can't be disposed.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
        cts.CancelAfter(_connectTimeout);

        Task shutdownRequested;
        Task? connectTask = null;

        try
        {
            try
            {
                (_, shutdownRequested) = await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (_disposedCts.IsCancellationRequested)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was aborted because the connection cache was disposed.");
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
            lock (_mutex)
            {
                // connectTask is executing this method and about to throw.
                connectTask = _pendingConnections[serverAddress].ConnectTask;
                _pendingConnections.Remove(serverAddress);
            }

            _ = DisposePendingConnectionAsync(connection, connectTask);
            throw;
        }

        lock (_mutex)
        {
            if (_shutdownTask is null)
            {
                // the connection is now "attached" in _activeConnections
                _activeConnections.Add(serverAddress, connection);
                _detachedConnectionCount--;
            }
            else
            {
                connectTask = _pendingConnections[serverAddress].ConnectTask;
            }
            bool removed = _pendingConnections.Remove(serverAddress);
            Debug.Assert(removed);
        }

        if (connectTask is null)
        {
            _ = ShutdownWhenRequestedAsync(connection, serverAddress, shutdownRequested);
        }
        else
        {
            // As soon as this method completes successfully, we shut down then dispose the connection.
            _ = DisposePendingConnectionAsync(connection, connectTask);
        }

        async Task DisposePendingConnectionAsync(IProtocolConnection connection, Task connectTask)
        {
            try
            {
                await connectTask.ConfigureAwait(false);

                // Since we own a detachedConnectionCount, _disposedCts is not disposed.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
                cts.CancelAfter(_shutdownTimeout);
                await connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Observe and ignore exceptions.
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

        async Task ShutdownWhenRequestedAsync(
            IProtocolConnection connection,
            ServerAddress serverAddress,
            Task shutdownRequested)
        {
            await shutdownRequested.ConfigureAwait(false);
            await RemoveFromActiveAsync(serverAddress, connection).ConfigureAwait(false);
        }
    }

    /// <summary>Gets an active connection, by creating and connecting (if necessary) a new protocol connection.
    /// </summary>
    /// <param name="serverAddressFeature">The server address feature.</param>
    /// <param name="cancellationToken">The cancellation token of the invocation calling this method.</param>
    private async Task<IProtocolConnection> GetActiveConnectionAsync(
        IServerAddressFeature serverAddressFeature,
        CancellationToken cancellationToken)
    {
        Debug.Assert(serverAddressFeature.ServerAddress is not null);
        Exception? connectionException = null;
        (IProtocolConnection Connection, Task ConnectTask) pendingConnectionValue;
        var enumerator = new ServerAddressEnumerator(serverAddressFeature);
        while (enumerator.MoveNext())
        {
            ServerAddress serverAddress = enumerator.Current;
            if (enumerator.CurrentIndex > 0)
            {
                // Rotate the server addresses before each new connection attempt after the initial attempt
                serverAddressFeature.RotateAddresses();
            }

            try
            {
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

                    if (!_pendingConnections.TryGetValue(serverAddress, out pendingConnectionValue))
                    {
                        connection = _connectionFactory.CreateConnection(serverAddress);
                        _detachedConnectionCount++;
                        pendingConnectionValue = (connection, CreateConnectTask(connection, serverAddress));
                        _pendingConnections.Add(serverAddress, pendingConnectionValue);
                    }
                }
                // ConnectTask itself takes care of scheduling its exception observation when it fails.
                await pendingConnectionValue.ConnectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                return pendingConnectionValue.Connection;
            }
            catch (TimeoutException exception)
            {
                connectionException = exception;
            }
            catch (IceRpcException exception) when (exception.IceRpcError is
                IceRpcError.ConnectionAborted or
                IceRpcError.ConnectionRefused or
                IceRpcError.ServerBusy or
                IceRpcError.ServerUnreachable)
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

        Debug.Assert(connectionException is not null);
        ExceptionDispatchInfo.Throw(connectionException);
        Debug.Assert(false);
        throw connectionException;
    }

    /// <summary>Removes the connection from _activeConnections, and when successful, shuts down and disposes this
    /// connection.</summary>
    /// <param name="serverAddress">The server address key in _activeConnections.</param>
    /// <param name="connection">The connection to shutdown and dispose after removal.</param>
    private Task RemoveFromActiveAsync(ServerAddress serverAddress, IProtocolConnection connection)
    {
        lock (_mutex)
        {
            if (_shutdownTask is null && _activeConnections.Remove(serverAddress))
            {
                // it's now our connection.
                _detachedConnectionCount++;
            }
            else
            {
                // Another task owns this connection
                return Task.CompletedTask;
            }
        }

        return ShutdownAndDisposeConnectionAsync();

        async Task ShutdownAndDisposeConnectionAsync()
        {
            // _disposedCts is not disposed since we own a detachedConnectionCount
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
            cts.CancelAfter(_shutdownTimeout);

            try
            {
                await connection.ShutdownAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore connection shutdown failures
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
    }

    /// <summary>Tries to get an existing connection matching one of the addresses of the server address feature.
    /// </summary>
    /// <param name="serverAddressFeature">The server address feature.</param>
    /// <param name="connection">When this method returns <see langword="true" /> it contains an active connection,
    /// otherwise, it is <see langword="null" />.</param>
    /// <returns><see langword="true" /> when an active connection matching any of the addresses of the server
    /// address feature is found, <see langword="false"/> otherwise.</returns>
    private bool TryGetActiveConnection(
        IServerAddressFeature serverAddressFeature,
        [NotNullWhen(true)] out IProtocolConnection? connection)
    {
        lock (_mutex)
        {
            connection = null;
            if (_disposeTask is not null)
            {
                throw new IceRpcException(IceRpcError.OperationAborted, "The connection cache was disposed.");
            }

            if (_shutdownTask is not null)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, "The connection cache was shut down.");
            }

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
                        serverAddressFeature.AltServerAddresses = serverAddressFeature.AltServerAddresses
                            .RemoveAt(enumerator.CurrentIndex - 1)
                            .Insert(0, serverAddressFeature.ServerAddress!.Value);
                        serverAddressFeature.ServerAddress = serverAddress;
                    }
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>A helper struct that implements an enumerator that allows iterating the addresses of an
    /// <see cref="IServerAddressFeature" /> without allocations.</summary>
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
