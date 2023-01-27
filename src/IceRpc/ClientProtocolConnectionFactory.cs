// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc;

/// <summary>The default implementation of <see cref="IClientProtocolConnectionFactory" />.</summary>
public sealed class ClientProtocolConnectionFactory : IClientProtocolConnectionFactory
{
    private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;
    private readonly ConnectionOptions _connectionOptions;
    private readonly IDuplexClientTransport _duplexClientTransport;
    private readonly DuplexConnectionOptions _duplexConnectionOptions;
    private readonly ILogger _logger;
    private readonly IMultiplexedClientTransport _multiplexedClientTransport;
    private readonly MultiplexedConnectionOptions _multiplexedConnectionOptions;

    /// <summary>Constructs a client protocol connection factory.</summary>
    /// <param name="connectionOptions">The connection options.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    /// <param name="duplexClientTransport">The duplex client transport. Null is equivalent to
    /// <see cref="IDuplexClientTransport.Default" />.</param>
    /// <param name="multiplexedClientTransport">The multiplexed client transport. Null is equivalent to
    /// <see cref="IMultiplexedClientTransport.Default" />.</param>
    /// <param name="logger">The logger.</param>
    public ClientProtocolConnectionFactory(
        ConnectionOptions connectionOptions,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        ILogger? logger = null)
    {
        _clientAuthenticationOptions = clientAuthenticationOptions;
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
            MaxBidirectionalStreams = connectionOptions.Dispatcher is null ? 0 :
                connectionOptions.MaxIceRpcBidirectionalStreams,

            // Add an additional stream for the icerpc protocol control stream.
            MaxUnidirectionalStreams = connectionOptions.Dispatcher is null ? 1 :
                connectionOptions.MaxIceRpcUnidirectionalStreams + 1,

            Pool = connectionOptions.Pool,
            MinSegmentSize = connectionOptions.MinSegmentSize,
        };

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
#pragma warning disable CA2000
        IProtocolConnection connection =
            serverAddress.Protocol == Protocol.Ice ?
                new IceProtocolConnection(
                    _duplexClientTransport.CreateConnection(
                        serverAddress,
                        _duplexConnectionOptions,
                        _clientAuthenticationOptions),
                    transportConnectionInformation: null,
                    _connectionOptions) :
                new IceRpcProtocolConnection(
                    _multiplexedClientTransport.CreateConnection(
                        serverAddress,
                        _multiplexedConnectionOptions,
                        _clientAuthenticationOptions),
                    transportConnectionInformation: null,
                    _connectionOptions);

        connection = new MetricsProtocolConnectionDecorator(connection, Metrics.ClientMetrics, logStart: true);

        return _logger == NullLogger.Instance ? connection :
            new LogProtocolConnectionDecorator(connection, remoteNetworkAddress: null, _logger);
#pragma warning restore CA2000
    }
}
