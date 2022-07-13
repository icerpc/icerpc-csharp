// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection
/// cannot be reconnected after being closed.</summary>
public sealed class ClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the default client transport for icerpc protocol connections.</summary>
    public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets the default client transport for ice protocol connections.</summary>
    public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
        new TcpClientTransport();

    /// <summary>Gets the endpoint of this connection.</summary>
    // TODO: should we remove this property?
    public Endpoint Endpoint { get; }

    /// <summary>Gets the network connection information or <c>null</c> if the connection is not connected.
    /// </summary>
    public NetworkConnectionInformation? NetworkConnectionInformation
    {
        get
        {
            lock (_mutex)
            {
                return _networkConnectionInformation;
            }
        }

        private set
        {
            lock (_mutex)
            {
                _networkConnectionInformation = value;
            }
        }
    }

    /// <summary>Gets the protocol of this connection.</summary>
    public Protocol Protocol => Endpoint.Protocol;

    private bool _isDisposed;

    private readonly object _mutex = new();

    private NetworkConnectionInformation? _networkConnectionInformation;

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
        IClientTransport<ISimpleNetworkConnection>? simpleClientTransport = null) =>
        _protocolConnection = ProtocolConnection.CreateClientConnection(
            options,
            loggerFactory,
            multiplexedClientTransport ?? DefaultMultiplexedClientTransport,
            simpleClientTransport ?? DefaultSimpleClientTransport);

    /// <summary>Constructs a client connection with the specified endpoint and authentication options.  All other
    /// properties have their default values.</summary>
    /// <param name="endpoint">The connection endpoint.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(Endpoint endpoint, SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            Endpoint = endpoint
        })
    {
    }

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
        CheckIfDisposed();
        NetworkConnectionInformation = await _protocolConnection.ConnectAsync(cancel).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _isDisposed = true;
        return _protocolConnection.DisposeAsync();
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        CheckIfDisposed();
        return _protocolConnection.InvokeAsync(request, cancel);
    }

    /// <summary>Adds a callback that will be executed when the connection with the peer is aborted.</summary>
    public void OnAbort(Action<Exception> callback)
    {
        CheckIfDisposed();
        _protocolConnection.OnAbort(callback);
    }

    /// <summary>Adds a callback that will be executed when the connection is shut down by the peer or the idle
    /// timeout.</summary>
    public void OnShutdown(Action<string> callback)
    {
        CheckIfDisposed();
        _protocolConnection.OnShutdown(callback);
    }

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
    public Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        CheckIfDisposed();
        return _protocolConnection.ShutdownAsync(message, cancel);
    }

    private void CheckIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ClientConnection));
        }
    }
}
