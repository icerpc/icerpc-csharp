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
    public NetworkConnectionInformation? NetworkConnectionInformation => _core.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol => RemoteEndpoint.Protocol;

    /// <inheritdoc/>
    public Endpoint RemoteEndpoint { get; }

    // Returns true if the connection establishment completed.
    private bool IsConnected => _connectTask?.IsCompleted ?? false;

    private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;

    private readonly CancellationTokenSource _connectCancellationSource = new();

    // _connectTask is set on connection establishment. It's volatile to avoid locking the mutex to check if the
    // connection establishment is completed.
    private volatile Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    private readonly ConnectionCore _core;
    private readonly ILoggerFactory _loggerFactory;

    private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;

    private readonly object _mutex = new();

    private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;

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
        _core = new ConnectionCore(options);
        _connectTimeout = options.ConnectTimeout;

        _clientAuthenticationOptions = options.ClientAuthenticationOptions;

        RemoteEndpoint = options.RemoteEndpoint ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.RemoteEndpoint)} is not set",
                nameof(options));

        _multiplexedClientTransport = multiplexedClientTransport ?? DefaultMultiplexedClientTransport;
        _simpleClientTransport = simpleClientTransport ?? DefaultSimpleClientTransport;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
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
    public void Abort() => _core.Abort(this);

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
                if (Protocol == Protocol.Ice)
                {
                    _connectTask = ConnectAsync(
                        _simpleClientTransport,
                        IceProtocol.Instance.ProtocolConnectionFactory,
                        LogSimpleNetworkConnectionDecorator.Decorate);
                }
                else
                {
                    _connectTask = ConnectAsync(
                         _multiplexedClientTransport,
                         IceRpcProtocol.Instance.ProtocolConnectionFactory,
                         LogMultiplexedNetworkConnectionDecorator.Decorate);
                }
            }
            else if (_connectTask.IsCompletedSuccessfully)
            {
                // Connection establishment completed successfully, we're done.
                return;
            }
            else if (_connectTask.IsCompleted)
            {
                // Connection establishment didn't complete successfully so at this point the connection is closed.
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

        async Task ConnectAsync<T>(
            IClientTransport<T> clientTransport,
            IProtocolConnectionFactory<T> protocolConnectionFactory,
            LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory)
            where T : INetworkConnection
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            // TODO: we create a cancellation token source just for the purpose if raising ConnectTimedException below.
            // Is it worth it? If not, we could just call _connectionCancellationSource.CancelAfter(_connectTimeout).
            using var connectTimeoutSource = new CancellationTokenSource(_connectTimeout);
            using var _ = connectTimeoutSource.Token.Register(_connectCancellationSource.Cancel);

            try
            {
                // This is the composition root of client Connections, where we install log decorators when logging
                // is enabled.

                ILogger logger = _loggerFactory.CreateLogger("IceRpc.Client");

                T networkConnection = clientTransport.CreateConnection(
                    RemoteEndpoint,
                    _clientAuthenticationOptions,
                    logger);

                // TODO: log level
                if (logger.IsEnabled(LogLevel.Error))
                {
                    networkConnection = logDecoratorFactory(networkConnection, RemoteEndpoint, isServer: false, logger);

                    protocolConnectionFactory =
                        new LogProtocolConnectionFactoryDecorator<T>(protocolConnectionFactory, logger);

                    _core.OnClose(
                        this,
                        (connection, exception) =>
                        {
                            if (NetworkConnectionInformation is NetworkConnectionInformation connectionInformation)
                            {
                                using IDisposable scope = logger.StartClientConnectionScope(connectionInformation);
                                logger.LogConnectionClosedReason(exception);
                            }
                        });
                }

                await _core.ConnectAsync(
                    this,
                    isServer: false,
                    networkConnection,
                    protocolConnectionFactory,
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
        return await _core.InvokeAsync(this, request, cancel).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void OnClose(Action<IConnection, Exception> callback) => _core.OnClose(this, callback);

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
                // Don't call ShutdownAsync before ConnectAsync completes on the core connection.
                await _connectTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }

        await _core.ShutdownAsync(this, message, cancel).ConfigureAwait(false);

        _connectCancellationSource.Dispose();
    }
}
