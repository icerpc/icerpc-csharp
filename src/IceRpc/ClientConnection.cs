// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses.</summary>
public sealed class ClientConnection : IClientConnection, IAsyncDisposable
{
    /// <summary>Gets the default client transport for icerpc protocol connections.</summary>
    public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets the default client transport for ice protocol connections.</summary>
    public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
        new TcpClientTransport();

    /// <inheritdoc/>
    public bool IsInvocable => _core.IsInvocable;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation => _core.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol => RemoteEndpoint.Protocol;

    /// <inheritdoc/>
    public Endpoint RemoteEndpoint { get; }

    /// <summary>Gets the state of the connection.</summary>
    public ConnectionState State => _core.State;

    private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;
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
        _core = new ConnectionCore(ConnectionState.NotConnected, options, options.IsResumable);

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
    /// <param name="authenticationOptions">The client authentication options.</param>
    public ClientConnection(Endpoint endpoint, SslClientAuthenticationOptions? authenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = authenticationOptions,
            RemoteEndpoint = endpoint
        })
    {
    }

    /// <summary>Aborts the connection. This methods switches the connection state to <see
    /// cref="ConnectionState.Closed"/>.</summary>
    public void Abort() => _core.Abort(this);

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
    public Task ConnectAsync(CancellationToken cancel = default) =>
        Protocol == Protocol.Ice ?
        _core.ConnectClientAsync(
            this,
            _simpleClientTransport,
            IceProtocol.Instance.ProtocolConnectionFactory,
            LogSimpleNetworkConnectionDecorator.Decorate,
            _loggerFactory,
            _clientAuthenticationOptions,
            cancel) :
        _core.ConnectClientAsync(
            this,
            _multiplexedClientTransport,
            IceRpcProtocol.Instance.ProtocolConnectionFactory,
            LogMultiplexedNetworkConnectionDecorator.Decorate,
            _loggerFactory,
            _clientAuthenticationOptions,
            cancel);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        await ShutdownAsync("connection disposed", new CancellationToken(canceled: true)).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Checks if the parameters of the provided endpoint are compatible with this client connection.
    /// Compatible means a client could reuse this client connection instead of establishing a new client
    /// connection.</summary>
    /// <param name="remoteEndpoint">The endpoint to check.</param>
    /// <returns><c>true</c> when this client connection is an active connection whose parameters are compatible
    /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
    /// <remarks>This method checks only the parameters of the endpoint; it does not check other properties.
    /// </remarks>
    public bool HasCompatibleParams(Endpoint remoteEndpoint) => _core.HasCompatibleParams(remoteEndpoint);

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IProtocolConnection? protocolConnection = _core.GetProtocolConnection();
        if (protocolConnection == null)
        {
            await ConnectAsync(cancel).ConfigureAwait(false);
        }
        return await _core.InvokeAsync(this, request, cancel).ConfigureAwait(false);
    }

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _core.ShutdownAsync(this, message, isResumable: false, cancel);
}
