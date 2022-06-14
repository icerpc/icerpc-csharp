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

    private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;

    // _connectRequired remains true as long as ConnectAsync has not returned successfully. Once _connectRequired is
    // false, the connection is active, shutting down or closed, and calling ConnectAsync again on this connection is
    // not necessary or useful.
    private volatile bool _connectRequired = true;

    private readonly ConnectionCore _core;

    private readonly ILoggerFactory _loggerFactory;

    private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;

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
        if (Protocol == Protocol.Ice)
        {
            await _core.ConnectClientAsync(
                this,
                _simpleClientTransport,
                IceProtocol.Instance.ProtocolConnectionFactory,
                LogSimpleNetworkConnectionDecorator.Decorate,
                _loggerFactory,
                _clientAuthenticationOptions,
                cancel).ConfigureAwait(false);
        }
        else
        {
            await _core.ConnectClientAsync(
                this,
                _multiplexedClientTransport,
                IceRpcProtocol.Instance.ProtocolConnectionFactory,
                LogMultiplexedNetworkConnectionDecorator.Decorate,
                _loggerFactory,
                _clientAuthenticationOptions,
                cancel).ConfigureAwait(false);
        }
        _connectRequired = false;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        new(ShutdownAsync("connection disposed", new CancellationToken(canceled: true)));

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (_connectRequired)
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
    public Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _core.ShutdownAsync(this, message, cancel);
}
