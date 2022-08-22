// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using System.Collections.Immutable;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection
/// cannot be reconnected after being closed.</summary>
public sealed class ClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the default client transport for icerpc protocol connections.</summary>
    public static IMultiplexedClientTransport DefaultMultiplexedClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets the default client transport for ice protocol connections.</summary>
    public static IDuplexClientTransport DefaultDuplexClientTransport { get; } = new TcpClientTransport();

    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport"/> property is always
    /// non-null.</value>
    public ServerAddress ServerAddress => _protocolConnection.ServerAddress;

    /// <summary>Gets the protocol of this connection.</summary>
    public Protocol Protocol => ServerAddress.Protocol;

    private readonly IProtocolConnection _protocolConnection;

    /// <summary>Constructs a client connection.</summary>
    /// <param name="options">The connection options.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol connections.
    /// </param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    public ClientConnection(
        ClientConnectionOptions options,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        IDuplexClientTransport? duplexClientTransport = null)
    {
        ServerAddress serverAddress = options.ServerAddress ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.ServerAddress)} is not set",
                nameof(options));

        var factory = new ClientProtocolConnectionFactory(
            options,
            options.ClientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport);

        _protocolConnection = factory.CreateConnection(serverAddress);
    }

    /// <summary>Constructs a client connection with the specified server address and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="serverAddress">The address of the server.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(
        ServerAddress serverAddress,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            ServerAddress = serverAddress
        })
    {
    }

    /// <summary>Constructs a client connection with the specified server address URI and authentication options. All
    /// other properties have their default values.</summary>
    /// <param name="serverAddressUri">A URI that represents the address of the server.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(Uri serverAddressUri, SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ServerAddress(serverAddressUri), clientAuthenticationOptions)
    {
    }

    /// <summary>Establishes the connection. This method can be called multiple times, even concurrently.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that provides the <see cref="TransportConnectionInformation"/> of the transport connection, once
    /// this connection is established. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionAbortedException"/>if the connection was aborted.</description></item>
    /// <item><description><see cref="ObjectDisposedException"/>if this connection is disposed.</description></item>
    /// <item><description><see cref="OperationCanceledException"/>if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException"/>if this connection attempt or a previous attempt exceeded
    /// <see cref="ConnectionOptions.ConnectTimeout"/>.</description></item>
    /// </list>
    /// </returns>
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel = default) =>
        _protocolConnection.ConnectAsync(cancel);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _protocolConnection.DisposeAsync();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        if (request.Features.Get<IServerAddressFeature>() is IServerAddressFeature serverAddressFeature)
        {
            if (serverAddressFeature.ServerAddress is ServerAddress mainServerAddress)
            {
                CheckRequestServerAddresses(mainServerAddress, serverAddressFeature.AltServerAddresses);
            }
        }
        else if (request.ServiceAddress.ServerAddress is ServerAddress mainServerAddress)
        {
            CheckRequestServerAddresses(mainServerAddress, request.ServiceAddress.AltServerAddresses);
        }
        // If the request has no server address at all, we let it through.

        return _protocolConnection.InvokeAsync(request, cancel);

        void CheckRequestServerAddresses(
            ServerAddress mainServerAddress,
            ImmutableList<ServerAddress> altServerAddresses)
        {
            if (ServerAddressComparer.OptionalTransport.Equals(mainServerAddress, ServerAddress))
            {
                return;
            }

            foreach (ServerAddress serverAddress in altServerAddresses)
            {
                if (ServerAddressComparer.OptionalTransport.Equals(serverAddress, ServerAddress))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"none of the server addresses of the request matches this connection's server address: {ServerAddress}");
        }
    }

    /// <summary>Adds a callback that will be executed when the connection is aborted.</summary>
    /// <param name="callback">The callback to run when the connection is aborted.</param>
    /// TODO: fix doc-comment
    public void OnAbort(Action<Exception> callback) => _protocolConnection.OnAbort(callback);

    /// <summary>Adds a callback that will be executed when the connection is shut down.</summary>
    /// <param name="callback">The callback to run when the connection is shutdown.</param>
    /// TODO: fix doc-comment
    public void OnShutdown(Action<string> callback) => _protocolConnection.OnShutdown(callback);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation was requested through the cancellation
    /// token.</exception>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("client connection shutdown", cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the server when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation was requested through the cancellation
    /// token.</exception>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _protocolConnection.ShutdownAsync(message, cancel);
}
