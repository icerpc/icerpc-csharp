// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc;

/// <summary>Default implementation of <see cref="IClientProtocolConnectionFactory" />.</summary>
public sealed class ClientProtocolConnectionFactory : IClientProtocolConnectionFactory
{
    private static readonly string[] _iceParams = ["t", "z"];

    private readonly ConnectionOptions _connectionOptions;
    private readonly IDuplexClientTransport _duplexClientTransport;
    private readonly DuplexConnectionOptions _duplexConnectionOptions;
    private readonly SslClientAuthenticationOptions? _iceClientAuthenticationOptions;
    private readonly SslClientAuthenticationOptions? _iceRpcClientAuthenticationOptions;
    private readonly ILogger _logger;
    private readonly IMultiplexedClientTransport _multiplexedClientTransport;
    private readonly MultiplexedConnectionOptions _multiplexedConnectionOptions;

    /// <summary>Constructs a client protocol connection factory.</summary>
    /// <param name="connectionOptions">The connection options.</param>
    /// <param name="connectTimeout">The connect timeout.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    /// <param name="duplexClientTransport">The duplex client transport. <see langword="null" /> is equivalent to <see
    /// cref="IDuplexClientTransport.Default" />.</param>
    /// <param name="multiplexedClientTransport">The multiplexed client transport. <see langword="null" /> is equivalent
    /// to <see cref="IMultiplexedClientTransport.Default" />.</param>
    /// <param name="logger">The logger. <see langword="null" /> is equivalent to <see cref="NullLogger.Instance" />.
    /// </param>
    public ClientProtocolConnectionFactory(
        ConnectionOptions connectionOptions,
        TimeSpan connectTimeout,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        ILogger? logger = null)
    {
        if (clientAuthenticationOptions?.ApplicationProtocols is not null)
        {
            throw new ArgumentException(
                "The ApplicationProtocols property of the SSL client authentication options must be null. The ALPN is set automatically based on the server address protocol.",
                nameof(clientAuthenticationOptions));
        }

        _connectionOptions = connectionOptions;

        _duplexClientTransport = duplexClientTransport ?? IDuplexClientTransport.Default;
        _duplexConnectionOptions = new DuplexConnectionOptions
        {
            Pool = connectionOptions.Pool,
            MinSegmentSize = connectionOptions.MinSegmentSize,
        };

        _multiplexedClientTransport = multiplexedClientTransport ?? IMultiplexedClientTransport.Default;

        // If the dispatcher is null, we don't allow the peer to open streams for incoming requests. The only stream
        // which is accepted locally is the control stream created by the peer.
        _multiplexedConnectionOptions = new MultiplexedConnectionOptions
        {
            HandshakeTimeout = connectTimeout,

            MaxBidirectionalStreams = connectionOptions.Dispatcher is null ? 0 :
                connectionOptions.MaxIceRpcBidirectionalStreams,

            // Add an additional stream for the icerpc protocol control stream.
            MaxUnidirectionalStreams = connectionOptions.Dispatcher is null ? 1 :
                connectionOptions.MaxIceRpcUnidirectionalStreams + 1,

            Pool = connectionOptions.Pool,
            MinSegmentSize = connectionOptions.MinSegmentSize,
        };

        // Clone and set ALPN for each protocol.
        if (clientAuthenticationOptions is not null)
        {
            _iceClientAuthenticationOptions = clientAuthenticationOptions.ShallowClone();
            _iceClientAuthenticationOptions.ApplicationProtocols = [Protocol.Ice.AlpnProtocol];

            _iceRpcClientAuthenticationOptions = clientAuthenticationOptions.ShallowClone();
            _iceRpcClientAuthenticationOptions.ApplicationProtocols = [Protocol.IceRpc.AlpnProtocol];
        }

        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Creates a protocol connection to the specified server address.</summary>
    /// <param name="serverAddress">The address of the server.</param>
    /// <returns>The new protocol connection.</returns>
    /// <remarks>The protocol connection returned by this factory method is not connected. The caller must call
    /// <see cref="IProtocolConnection.ConnectAsync" /> exactly once on this connection before calling
    /// <see cref="IInvoker.InvokeAsync" />.</remarks>
    public IProtocolConnection CreateConnection(ServerAddress serverAddress)
    {
        var transportAddress = new TransportAddress
            {
                Host = serverAddress.Host,
                Port = serverAddress.Port,
                TransportName = serverAddress.Transport,
                Params = serverAddress.Params
            };

        IProtocolConnection connection;
        if (serverAddress.Protocol == Protocol.Ice)
        {
            SslClientAuthenticationOptions? clientAuthenticationOptions = _iceClientAuthenticationOptions;
            if (clientAuthenticationOptions is null && _duplexClientTransport.IsSslRequired(serverAddress.Transport))
            {
                clientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [Protocol.Ice.AlpnProtocol]
                };
            }

            // Strip Ice-specific params ("t", "z") before passing to transport. These params have no
            // effect with IceRPC transports.
            transportAddress = transportAddress with
            {
                Params = transportAddress.Params.RemoveRange(_iceParams)
            };

            connection = new IceProtocolConnection(
                _duplexClientTransport.CreateConnection(transportAddress, _duplexConnectionOptions, clientAuthenticationOptions),
                transportConnectionInformation: null,
                _connectionOptions);
        }
        else
        {
            SslClientAuthenticationOptions? clientAuthenticationOptions = _iceRpcClientAuthenticationOptions;
            if (clientAuthenticationOptions is null && _multiplexedClientTransport.IsSslRequired(serverAddress.Transport))
            {
                clientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [Protocol.IceRpc.AlpnProtocol]
                };
            }

            connection = new IceRpcProtocolConnection(
                _multiplexedClientTransport.CreateConnection(transportAddress, _multiplexedConnectionOptions, clientAuthenticationOptions),
                transportConnectionInformation: null,
                _connectionOptions,
                taskExceptionObserver: _logger == NullLogger.Instance ? null :
                    new LogTaskExceptionObserver(_logger));
        }

        connection = new MetricsProtocolConnectionDecorator(connection, Metrics.ClientMetrics, connectStarted: false);

        return _logger == NullLogger.Instance ? connection :
            new LogProtocolConnectionDecorator(connection, serverAddress, remoteNetworkAddress: null, _logger);
    }
}
