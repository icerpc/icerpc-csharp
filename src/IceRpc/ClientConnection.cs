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

            _protocolConnection = new IceProtocolConnection(networkConnection, isServer: false, options);
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
#pragma warning disable CA2000 // bogus warning, the decorator is disposed by IceRpcProtocolConnection
                networkConnection = new LogMultiplexedNetworkConnectionDecorator(
                    networkConnection,
                    RemoteEndpoint,
                    isServer: false,
                    logger);
#pragma warning restore CA2000
            }

            _protocolConnection = new IceRpcProtocolConnection(networkConnection, options);
        }

        // TODO: log level
        if (logger.IsEnabled(LogLevel.Error))
        {
            _protocolConnection = new LogProtocolConnectionDecorator(_protocolConnection, isServer: false, logger);
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
    public async Task ConnectAsync(CancellationToken cancel = default) =>
        NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
            connection: this,
            cancel: cancel).ConfigureAwait(false);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _protocolConnection.DisposeAsync();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default) =>
        _protocolConnection.InvokeAsync(request, this, cancel);

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
    public Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _protocolConnection.ShutdownAsync(message, cancel);
}
