// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
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

    // Prevent concurrent assignment of _connectTask and _shutdownTask.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly CancellationTokenSource _shutdownCancellationSource = new();

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
                if (_shutdownTask != null)
                {
                    throw new ConnectionClosedException();
                }
                else if (_connectTask == null)
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
        catch (OperationCanceledException exception)
        {
            if (exception.CancellationToken == cancel)
            {
                throw;
            }
            else
            {
                // Unlikely, but we prefer throwing OperationCanceledException (if appropriate) over
                // ConnectionCanceledException.
                cancel.ThrowIfCancellationRequested();

                // exception comes from another call to ConnectAsync
                throw new ConnectionCanceledException();
            }
        }

        async Task PerformConnectAsync(CancellationToken cancel)
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
               isServer: false,
               this,
               cancel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        new(ShutdownAsync("connection disposed", new CancellationToken(canceled: true)));

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
                if (_connectTask is null)
                {
                    using var cancellationTokenSource = new CancellationTokenSource(_connectTimeout);

                    try
                    {
                        await ConnectAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException("connect timeout exceeded");
                    }
                }
                else
                {
                    try
                    {
                        await _connectTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new ConnectionCanceledException();
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public void OnClose(Action<Exception> callback) => _protocolConnection.OnClose(callback);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public async Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        Task? connectTask = null;
        lock (_mutex)
        {
            if (_connectTask == null)
            {
                _shutdownTask = Task.CompletedTask;
            }
            else
            {
                connectTask = _connectTask;

                // Calling shutdown on the protocol connection is required even if it's not connected because
                // ShutdownAsync is used for two purposes: graceful protocol shutdown and the disposal of the
                // connection's resources.
                _shutdownTask ??= ShutdownAsyncCore();
            }
        }

        // Cancel shutdown if the given token is canceled.
        using CancellationTokenRegistration _ = cancel.Register(
            () =>
            {
                try
                {
                    _shutdownCancellationSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            });

        if (connectTask == null)
        {
            // ConnectAsync wasn't called, just release resources associated with the protocol connection.
            // TODO: Refactor depending on what we decide for the protocol connection resource cleanup (#1397,
            // #1372, #1404, #1400).
            _protocolConnection.Abort(new ConnectionClosedException());

            // Release disposable resources.
            _shutdownCancellationSource.Dispose();
        }
        else
        {
            // Wait for the shutdown to complete.
            try
            {
                await _shutdownTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore. This can occur if the protocol connection is aborted while the shutdown is in progress.
            }
        }

        async Task ShutdownAsyncCore()
        {
            // Make sure we shutdown the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            try
            {
                // Wait for connection establishment to complete before calling ShutdownAsync.
                await connectTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Protocol connection resource cleanup. This is for now performed by Abort (which should have
                // been named CloseAsync like ConnectionCore.CloseAsync).
                // TODO: Refactor depending on what we decide for the protocol connection resource cleanup (#1397,
                // #1372, #1404, #1400).
                _protocolConnection.Abort(exception);
                return;
            }

            // If shutdown times out, abort the protocol connection.
            using var shutdownTimeoutCancellationSource = new CancellationTokenSource(_shutdownTimeout);
            using CancellationTokenRegistration _ = shutdownTimeoutCancellationSource.Token.Register(Abort);

            // Shutdown the connection.
            await _protocolConnection.ShutdownAsync(message, _shutdownCancellationSource.Token).ConfigureAwait(false);

            // Release disposable resources.
            _shutdownCancellationSource.Dispose();
        }
    }
}
