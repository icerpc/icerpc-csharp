// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>Gets the endpoint of this connection.</summary>
    /// <value>The endpoint (server address) of this connection. It has a non-null <see cref="Endpoint.Transport"/> even
    /// when <see cref="ClientConnectionOptions.Endpoint"/> does not.</value>
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
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    public ClientConnection(
        ClientConnectionOptions options,
        ILoggerFactory? loggerFactory = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        IDuplexClientTransport? duplexClientTransport = null)
    {
        Endpoint endpoint = options.Endpoint ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.Endpoint)} is not set",
                nameof(options));

        // This is the composition root of client Connections, where we install log decorators when logging is enabled.

        ILogger logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(GetType().FullName!);

        if (options.Dispatcher is IDispatcher dispatcher && logger != NullLogger.Instance)
        {
            options = options with { Dispatcher = new LogDispatcherDecorator(dispatcher, logger) };
        }

        ProtocolConnection decoratee;

        if (endpoint.Protocol == Protocol.Ice)
        {
            duplexClientTransport ??= DefaultDuplexClientTransport;
            if (logger != NullLogger.Instance)
            {
                duplexClientTransport = new LogDuplexClientTransportDecorator(duplexClientTransport, logger);
            }

            IDuplexConnection transportConnection = duplexClientTransport.CreateConnection(
                new DuplexClientConnectionOptions
                {
                    Pool = options.Pool,
                    MinSegmentSize = options.MinSegmentSize,
                    Endpoint = endpoint,
                    ClientAuthenticationOptions = options.ClientAuthenticationOptions,
                    Logger = logger
                });

            Endpoint = transportConnection.Endpoint;

#pragma warning disable CA2000
            decoratee = new IceProtocolConnection(transportConnection, isServer: false, options);
#pragma warning restore CA2000
        }
        else
        {
            multiplexedClientTransport ??= DefaultMultiplexedClientTransport;
            if (logger != NullLogger.Instance)
            {
                multiplexedClientTransport = new LogMultiplexedClientTransportDecorator(
                    multiplexedClientTransport,
                    logger);
            }

            IMultiplexedConnection transportConnection = multiplexedClientTransport.CreateConnection(
                new MultiplexedClientConnectionOptions
                {
                    MaxBidirectionalStreams =
                        options.Dispatcher is null ? 0 : options.MaxIceRpcBidirectionalStreams,
                    // Add an additional stream for the icerpc protocol control stream.
                    MaxUnidirectionalStreams =
                        options.Dispatcher is null ? 1 : (options.MaxIceRpcUnidirectionalStreams + 1),
                    Pool = options.Pool,
                    MinSegmentSize = options.MinSegmentSize,
                    Endpoint = endpoint,
                    ClientAuthenticationOptions = options.ClientAuthenticationOptions,
                    Logger = logger,
                    StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter
                });

            Endpoint = transportConnection.Endpoint;

#pragma warning disable CA2000
            decoratee = new IceRpcProtocolConnection(transportConnection, options);
#pragma warning restore CA2000
        }

        if (logger != NullLogger.Instance)
        {
            _protocolConnection = new LogProtocolConnectionDecorator(decoratee, logger);
            decoratee.Decorator = _protocolConnection;
        }
        else
        {
            _protocolConnection = decoratee;
        }
    }

    /// <summary>Constructs a client connection with the specified endpoint and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="endpoint">The address of the server.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(Endpoint endpoint, SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            Endpoint = endpoint
        })
    {
    }

    /// <summary>Constructs a client connection with the specified endpoint URI and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="endpointUri">A URI that represents the address of the server.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(Uri endpointUri, SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new Endpoint(endpointUri), clientAuthenticationOptions)
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
        if (request.Features.Get<IEndpointFeature>() is IEndpointFeature endpointFeature)
        {
            if (endpointFeature.Endpoint is Endpoint mainEndpoint)
            {
                CheckRequestEndpoints(mainEndpoint, endpointFeature.AltEndpoints);
            }
        }
        else if (request.ServiceAddress.Endpoint is Endpoint mainEndpoint)
        {
            CheckRequestEndpoints(mainEndpoint, request.ServiceAddress.AltEndpoints);
        }
        // If the request has no endpoint at all, we let it through.

        return _protocolConnection.InvokeAsync(request, cancel);

        void CheckRequestEndpoints(Endpoint mainEndpoint, ImmutableList<Endpoint> altEndpoints)
        {
            if (EndpointComparer.OptionalTransport.Equals(mainEndpoint, Endpoint))
            {
                return;
            }

            foreach (Endpoint endpoint in altEndpoints)
            {
                if (EndpointComparer.OptionalTransport.Equals(endpoint, Endpoint))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"none of the request's endpoint(s) matches this connection's endpoint: {Endpoint}");
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
