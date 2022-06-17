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

    // Returns true if the connection establishment completed.
    private bool IsConnected => _connectTask?.IsCompleted ?? false;

    private readonly CancellationTokenSource _connectCancellationSource = new();

    // _connectTask is set on connection establishment. It's volatile to avoid locking the mutex to check if the
    // connection establishment is completed.
    private volatile Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    // Prevent concurrent assignment of _connectTask.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

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

        RemoteEndpoint = options.RemoteEndpoint ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.RemoteEndpoint)} is not set",
                nameof(options));

        // This is the composition root of client Connections, where we install log decorators when logging is enabled.

        multiplexedClientTransport = multiplexedClientTransport ?? DefaultMultiplexedClientTransport;
        simpleClientTransport = simpleClientTransport ?? DefaultSimpleClientTransport;

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
    public void Abort() => _protocolConnection.Abort(this, new ConnectionAbortedException());

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            if (_connectTask == null)
            {
                _connectTask = ConnectAsync();
            }
            else if (_connectTask.IsCompletedSuccessfully)
            {
                // Connection establishment completed successfully, we're done.
                return;
            }
            else if (_connectTask.IsCanceled || _connectTask.IsFaulted)
            {
                // Connection establishment didn't complete successfully. Since we're using an already closed connection
                // we raise ConnectionClosedException instead of raising the connection establishment failure exception.
                throw new ConnectionClosedException();
            }
            else
            {
                // Connection establishment is in progress, wait for _connectTask to complete below.
            }
        }

        // Cancel the connection establishment if the given token is canceled.
        using CancellationTokenRegistration _ = cancel.Register(_connectCancellationSource!.Cancel);

        // Wait for the connection establishment to complete.
        await _connectTask.ConfigureAwait(false);

        async Task ConnectAsync()
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            // TODO: we create a cancellation token source just for the purpose if raising ConnectTimedException below.
            // Is it worth it? If not, we could just call _connectionCancellationSource.CancelAfter(_connectTimeout).
            using var connectTimeoutSource = new CancellationTokenSource(_connectTimeout);
            using var _ = connectTimeoutSource.Token.Register(_connectCancellationSource.Cancel);

            try
            {
                NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
                    this,
                    isServer: false,
                    _connectCancellationSource.Token).ConfigureAwait(false);
            }
            catch when (connectTimeoutSource.IsCancellationRequested)
            {
                throw new ConnectTimeoutException();
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        new(ShutdownAsync("connection disposed", new CancellationToken(canceled: true)));

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (!IsConnected)
        {
            await ConnectAsync(cancel).ConfigureAwait(false);
        }
        return await _protocolConnection.InvokeAsync(this, request, cancel).ConfigureAwait(false);
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
        if (!IsConnected)
        {
            lock (_mutex)
            {
                // Make sure that connection establishment is not initiated if it's not in progress or completed.
                _connectTask ??= Task.FromException(new ConnectionClosedException());
            }

            // TODO: Should we actually cancel the pending connect on ShutdownAsync?
            // try
            // {
            //     _connectCancellationSource.Cancel();
            // }
            // catch
            // {
            // }

            try
            {
                // Wait for connection establishment to complete before calling ShutdownAsync.
                await _connectTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }

        await _protocolConnection.ShutdownAsync(this, message, cancel).ConfigureAwait(false);

        _connectCancellationSource.Dispose();
    }
}
