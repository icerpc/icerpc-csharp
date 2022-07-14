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
public sealed class ClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the default client transport for icerpc protocol connections.</summary>
    public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets the default client transport for ice protocol connections.</summary>
    public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
        new TcpClientTransport();

    /// <summary>Gets the endpoint of this connection. This endpoint includes a transport parameter even when
    /// <see cref="ClientConnectionOptions.Endpoint"/> does not.</summary>
    public Endpoint Endpoint { get; }

    /// <summary>Gets the protocol of this connection.</summary>
    public Protocol Protocol => Endpoint.Protocol;

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
        Endpoint endpoint = options.Endpoint ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.Endpoint)} is not set",
                nameof(options));

        // This is the composition root of client Connections, where we install log decorators when logging is enabled.

        multiplexedClientTransport ??= DefaultMultiplexedClientTransport;
        simpleClientTransport ??= DefaultSimpleClientTransport;

        ILogger logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc.Client");

        if (endpoint.Protocol == Protocol.Ice)
        {
            ISimpleNetworkConnection networkConnection = simpleClientTransport.CreateConnection(
                endpoint,
                options.ClientAuthenticationOptions,
                logger);

            Endpoint = networkConnection.Endpoint;

            // TODO: log level
            if (logger.IsEnabled(LogLevel.Error))
            {
                networkConnection = new LogSimpleNetworkConnectionDecorator(
                    networkConnection,
                    Endpoint,
                    isServer: false,
                    logger);
            }

            _protocolConnection = new IceProtocolConnection(networkConnection, isServer: false, options);
        }
        else
        {
            IMultiplexedNetworkConnection networkConnection = multiplexedClientTransport.CreateConnection(
                endpoint,
                options.ClientAuthenticationOptions,
                logger);

            Endpoint = networkConnection.Endpoint;

            // TODO: log level
            if (logger.IsEnabled(LogLevel.Error))
            {
#pragma warning disable CA2000 // bogus warning, the decorator is disposed by IceRpcProtocolConnection
                networkConnection = new LogMultiplexedNetworkConnectionDecorator(
                    networkConnection,
                    Endpoint,
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
    /// <returns>A task that provides the <see cref="NetworkConnectionInformation"/> of the transport connection, once
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
    public Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel = default) =>
        _protocolConnection.ConnectAsync(cancel);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _protocolConnection.DisposeAsync();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default) =>
        _protocolConnection.InvokeAsync(request, cancel);

    /// <summary>Adds a callback that will be executed when the connection is aborted.</summary>
    /// TODO: fix doc-comment
    public void OnAbort(Action<Exception> callback) => _protocolConnection.OnAbort(callback);

    /// <summary>Adds a callback that will be executed when the connection is shut down.</summary>
    /// TODO: fix doc-comment
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
