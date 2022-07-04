// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection
/// cannot be reconnected after being closed.</summary>
public sealed class ClientConnection : IClientConnection, IAsyncDisposable
{
    /// <summary>Gets the default client transport for icerpc protocol connections.</summary>
    public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets the default client transport for ice protocol connections.</summary>
    public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
        new TcpClientTransport();

    /// <inheritdoc/>
    public bool IsResumable => false;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    /// <inheritdoc/>
    public Protocol Protocol => RemoteEndpoint.Protocol;

    /// <inheritdoc/>
    public Endpoint RemoteEndpoint { get; }

    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;
    private readonly CancellationTokenSource _connectTokenSource = new();

    private Task? _disposeTask;

    // Prevent concurrent assignment of _connectTask and _shutdownTask.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly CancellationTokenSource _protocolConnectionCancellationSource = new(); // TODO temporary

    private Task? _shutdownTask;

    private readonly TimeSpan _shutdownTimeout;
    private readonly CancellationTokenSource _shutdownTokenSource = new();

    /// <summary>Constructs a client connection.</summary>
    /// <param name="options">The connection options.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
    /// </param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol connections.
    /// </param>
    /// <param name="simpleClientTransport">The simple transport used to create ice protocol connections.</param>
    public ClientConnection(
        ClientConnectionOptions options,
        ILoggerFactory? loggerFactory = null,
        IClientTransport<IMultiplexedNetworkConnection>? multiplexedClientTransport = null,
        IClientTransport<ISimpleNetworkConnection>? simpleClientTransport = null)
    {
        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;

        RemoteEndpoint = options.RemoteEndpoint ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.RemoteEndpoint)} is not set",
                nameof(options));

        // This is the composition root of client Connections, where we install log decorators when logging is enabled.

        multiplexedClientTransport ??= DefaultMultiplexedClientTransport;
        simpleClientTransport ??= DefaultSimpleClientTransport;

        ILogger logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc.Client");

        if (Protocol == Protocol.Ice)
        {
            ISimpleNetworkConnection networkConnection = simpleClientTransport.CreateConnection(
                RemoteEndpoint,
                options.ClientAuthenticationOptions,
                logger);

            // TODO: log level
            if (logger.IsEnabled(LogLevel.Error))
            {
                networkConnection = new LogSimpleNetworkConnectionDecorator(
                    networkConnection,
                    RemoteEndpoint,
                    isServer: false,
                    logger);
            }

            _protocolConnection = new IceProtocolConnection(networkConnection, options);
        }
        else
        {
            IMultiplexedNetworkConnection networkConnection = multiplexedClientTransport.CreateConnection(
                RemoteEndpoint,
                options.ClientAuthenticationOptions,
                logger);

            // TODO: log level
            if (logger.IsEnabled(LogLevel.Error))
            {
                networkConnection = new LogMultiplexedNetworkConnectionDecorator(
                    networkConnection,
                    RemoteEndpoint,
                    isServer: false,
                    logger);
            }

            _protocolConnection = new IceRpcProtocolConnection(networkConnection, options);
        }

        // TODO: log level
        if (logger.IsEnabled(LogLevel.Error))
        {
            _protocolConnection = new LogProtocolConnectionDecorator(_protocolConnection, logger);
        }
    }

    /// <summary>Constructs a client connection with the specified remote endpoint and  authentication options.
    /// All other properties have their default values.</summary>
    /// <param name="endpoint">The connection remote endpoint.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(Endpoint endpoint, SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            RemoteEndpoint = endpoint
        })
    {
    }

    /// <summary>Aborts the connection.</summary>
    public void Abort() => _protocolConnection.Abort(new ConnectionAbortedException());

    /// <summary>Establishes the connection. This method can be called multiple times, even concurrently.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that represents the completion of the connect operation. This task can complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionAbortedException"/>if the connection was aborted.</description></item>
    /// <item><description><see cref="ObjectDisposedException"/>if this connection is disposed.</description></item>
    /// <item><description><see cref="OperationCanceledException"/>if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException"/>if this connection attempt or a previous attempt exceeded
    /// <see cref="ConnectionOptions.ConnectTimeout"/>.</description></item>
    /// </list>
    /// </returns>
    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            ThrowIfDisposed();
            _connectTask ??= ConnectAsyncCore(_connectTokenSource.Token);
        }

        using CancellationTokenRegistration _ = cancel.Register(
            () =>
            {
                try
                {
                    _connectTokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // ignored
                }
            });

        try
        {
            await _connectTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancel.ThrowIfCancellationRequested();

            // _connectTask was canceled through another token
            throw new ConnectionAbortedException();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // DisposeAsync can be called concurrently. For example, ConnectionPool can dispose a connection because the
        // server is shutting down and at the same time or shortly after dispose the same connection because of its own
        // disposal. We want to second disposal to "hang" if there is (for example) a bug in the dispatch code that
        // causes the DisposeAsync to hang.

        lock (_mutex)
        {
            _disposeTask ??= PerformDisposeAsync();
        }

        await _disposeTask.ConfigureAwait(false);

        async Task PerformDisposeAsync()
        {
            await Task.Yield();

            // TODO: temporary way to cancel dispatches and abort invocations.
            _protocolConnectionCancellationSource.Cancel();

            try
            {
                await ShutdownAsync("dispose client connection", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            // TODO: await _protocolConnection.DisposeAsync();
            _protocolConnectionCancellationSource.Dispose();

            _connectTokenSource.Dispose();
            _shutdownTokenSource.Dispose();
        }
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        return _connectTask is not null && _connectTask.IsCompletedSuccessfully ?
            _protocolConnection.InvokeAsync(request, this, cancel) :
            PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            await ConnectAsync(CancellationToken.None).WaitAsync(cancel).ConfigureAwait(false);
            return await _protocolConnection.InvokeAsync(request, this, cancel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void OnAbort(Action<Exception> callback) => _protocolConnection.OnAbort(callback);

    /// <inheritdoc/>
    public void OnShutdown(Action<string> callback) => _protocolConnection.OnShutdown(callback);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation was requested through the cancellation
    /// token.</exception>
    public Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("client connection shutdown", cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the server when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation was requested through the cancellation
    /// token.</exception>
    public async Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            ThrowIfDisposed();

            if (_shutdownTask is null)
            {
                if (_connectTask is null)
                {
                    // we have nothing else to do - this connection was never established
                    _connectTask = Task.FromException(new ConnectionClosedException());
                    return; // ShutdownAsync complete
                }

                _shutdownTask = ShutdownAsyncCore(message, _shutdownTokenSource.Token);
            }
        }

        using CancellationTokenRegistration _ = cancel.Register(
            () =>
            {
                try
                {
                    _shutdownTokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // ignored
                }
            });
        try
        {
            await _shutdownTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancel.ThrowIfCancellationRequested();

            // _shutdownTask was canceled through another token
            throw new ConnectionAbortedException();
        }
    }

    private async Task ConnectAsyncCore(CancellationToken cancel)
    {
        // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
        await Task.Yield();

        // Need second token to figure out if the call exceeded _connectTimeout or was canceled for another reason:
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linkedTokenSource.CancelAfter(_connectTimeout);
        try
        {
            // TODO: not quite correct because assignment to NetworkConnectionInformation is not atomic
            NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
                isServer: false,
                this,
                linkedTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Triggered by the CancelAfter above
            var timeoutException = new TimeoutException(
                $"connection establishment timed out after {_connectTimeout.TotalSeconds}s");
            _protocolConnection.Abort(timeoutException);
            throw timeoutException;
        }
        catch (Exception exception)
        {
            _protocolConnection.Abort(exception);
            throw;
        }
    }

    private async Task ShutdownAsyncCore(string message, CancellationToken cancel)
    {
        // Make sure we shutdown the connection asynchronously without holding any mutex lock from the caller.
        await Task.Yield();

        // Need second token to figure out if the call exceeded _shutdownTimeout or was canceled for another reason:
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linkedTokenSource.CancelAfter(_shutdownTimeout);

        try
        {
            // Wait for connection establishment to complete before calling ShutdownAsync.
            await ConnectAsync(linkedTokenSource.Token).ConfigureAwait(false);

            // Shut down the protocol connection.
            await _protocolConnection
                .ShutdownAsync(message, _protocolConnectionCancellationSource.Token) // currently does not throw anything
                .WaitAsync(linkedTokenSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Triggered by the CancelAfter above
            var timeoutException = new TimeoutException(
                $"connection shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
            _protocolConnection.Abort(timeoutException);
            throw timeoutException;
        }
        catch (Exception exception)
        {
            _protocolConnection.Abort(exception);
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        // Must be called with _mutex locked.
        if (_disposeTask is Task disposeTask && disposeTask.IsCompleted)
        {
            throw new ObjectDisposedException($"{typeof(ClientConnection)}");
        }
    }
}
