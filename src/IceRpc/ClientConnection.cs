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

    private bool _isDisposed;

    // Prevent concurrent assignment of _connectTask and _shutdownTask.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly CancellationTokenSource _protocolConnectionCancellationSource = new(); // TODO temporary

    private Task? _shutdownTask;

    private readonly TimeSpan _shutdownTimeout;

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
        _shutdownTimeout = options.CloseTimeout;

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

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    /// <exception cref="ConnectionCanceledException">Thrown if another call to this method was canceled.</exception>
    /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if cancellation was requested through the cancellation
    /// token.</exception>
    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        Task task;

        if (_connectTask is Task connectTask)
        {
            task = connectTask.WaitAsync(cancel);
        }
        else
        {
            lock (_mutex)
            {
                ThrowIfDisposed();

                if (_shutdownTask is not null)
                {
                    throw new ConnectionClosedException();
                }
                else if (_connectTask is null)
                {
                    _connectTask = PerformConnectAsync(cancel);
                    task = _connectTask;
                }
                else
                {
                    task = _connectTask.WaitAsync(cancel);
                }
            }
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken != cancel)
        {
            // exception comes from another call to ConnectAsync
            throw new ConnectionCanceledException();
        }

        async Task PerformConnectAsync(CancellationToken cancel)
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            // TODO: not quite correct because assignment to NetworkConnectionInformation is not atomic
            NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
                isServer: false,
                this,
                cancel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                return;
            }
        }

        // DisposeAsync first attempts a fully graceful shutdown that does not cancel dispatches or aborts invocations.
        using var tokenSource = new CancellationTokenSource(_shutdownTimeout);
        try
        {
            await ShutdownAsync("dispose client connection", tokenSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _protocolConnection.Abort(exception);
        }

        // TODO: await _protocolConnection.DisposeAsync();
        _protocolConnectionCancellationSource.Dispose();

        lock (_mutex)
        {
            _isDisposed = true;
        }
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        return _connectTask is not null && _connectTask.IsCompletedSuccessfully ?
            _protocolConnection.InvokeAsync(request, this, cancel) :
            PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            await PerformConnectAsync().WaitAsync(cancel).ConfigureAwait(false);
            return await _protocolConnection.InvokeAsync(request, this, cancel).ConfigureAwait(false);

            async Task PerformConnectAsync()
            {
                using var connectTimeoutTokenSource = new CancellationTokenSource(_connectTimeout);
                try
                {
                    await ConnectAsync(connectTimeoutTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("connect timeout exceeded");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void OnClose(Action<Exception> callback) => _protocolConnection.OnClose(callback);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="ConnectionCanceledException">Thrown if this connection was canceled by another call.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation was requested through the cancellation
    /// token.</exception>
    public Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("client connection shutdown", cancel: cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the server when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="ConnectionCanceledException">Thrown if this connection was canceled by another call.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation was requested through the cancellation
    /// token.</exception>
    public async Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        Task task;

        lock (_mutex)
        {
            ThrowIfDisposed();

            if (_shutdownTask is null)
            {
                _shutdownTask = PerformShutdownAsync(message, _connectTask, cancel);
                task = _shutdownTask;
            }
            else
            {
                task = _shutdownTask.WaitAsync(cancel);
            }
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken != cancel)
        {
            // A previous call to ShutdownAsync failed with OperationCanceledException
            throw new ConnectionCanceledException();
        }

        async Task PerformShutdownAsync(string message, Task? connectTask, CancellationToken cancel)
        {
            // Make sure we shutdown the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            if (connectTask is not null)
            {
                // Wait for connection establishment to complete before calling ShutdownAsync.
                await connectTask.WaitAsync(cancel).ConfigureAwait(false);
            }

            // Shut down the protocol connection.
            await _protocolConnection
                .ShutdownAsync(message, _protocolConnectionCancellationSource.Token) // currently does not throw anything
                .WaitAsync(cancel)
                .ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        // Must be called with _mutex locked.
        if (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(ClientConnection)}");
        }
    }
}
