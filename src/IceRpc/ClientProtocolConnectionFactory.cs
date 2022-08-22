// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Net.Security;

namespace IceRpc;

/// <summary>The default implementation of <see cref="IClientProtocolConnectionFactory"/>.</summary>
public class ClientProtocolConnectionFactory : IClientProtocolConnectionFactory
{
    /// <summary>Gets the default client transport for ice protocol connections.</summary>
    public static IDuplexClientTransport DefaultDuplexClientTransport { get; } = new TcpClientTransport();

    /// <summary>Gets the default client transport for icerpc protocol connections.</summary>
    public static IMultiplexedClientTransport DefaultMultiplexedClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;
    private readonly ConnectionOptions _connectionOptions;
    private readonly IDuplexClientTransport _duplexClientTransport;
    private readonly DuplexConnectionOptions _duplexConnectionOptions;
    private readonly IMultiplexedClientTransport _multiplexedClientTransport;
    private readonly MultiplexedConnectionOptions _multiplexedConnectionOptions;

    /// <summary>Constructs a client protocol connection factory.</summary>
    /// <param name="connectionOptions">The connection options.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    /// <param name="duplexClientTransport">The duplex client transport. Null is equivalent to
    /// <see cref="DefaultDuplexClientTransport"/>.</param>
    /// <param name="multiplexedClientTransport">The multiplexed client transport. Null is equivalent to
    /// <see cref="DefaultMultiplexedClientTransport"/>.</param>
    public ClientProtocolConnectionFactory(
        ConnectionOptions connectionOptions,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null)
    {
        _clientAuthenticationOptions = clientAuthenticationOptions;
        _connectionOptions = connectionOptions;

        _duplexClientTransport = duplexClientTransport ?? DefaultDuplexClientTransport;
        _duplexConnectionOptions = new DuplexConnectionOptions
        {
            Pool = connectionOptions.Pool,
            MinSegmentSize = connectionOptions.MinSegmentSize,
        };

        _multiplexedClientTransport = multiplexedClientTransport ?? DefaultMultiplexedClientTransport;
        _multiplexedConnectionOptions = new MultiplexedConnectionOptions
        {
            MaxBidirectionalStreams = connectionOptions.Dispatcher is null ? 0 :
                connectionOptions.MaxIceRpcBidirectionalStreams,

            // Add an additional stream for the icerpc protocol control stream.
            MaxUnidirectionalStreams = connectionOptions.Dispatcher is null ? 1 :
                connectionOptions.MaxIceRpcUnidirectionalStreams + 1,

            Pool = connectionOptions.Pool,
            MinSegmentSize = connectionOptions.MinSegmentSize,
            StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter
        };
    }

    /// <inheritdoc/>
    public IProtocolConnection CreateConnection(ServerAddress serverAddress) =>
        serverAddress.Protocol == Protocol.Ice ?
            new IceProtocolConnection(
                _duplexClientTransport.CreateConnection(
                    serverAddress,
                    _duplexConnectionOptions,
                    _clientAuthenticationOptions),
                isServer: false,
                _connectionOptions) :
            new IceRpcProtocolConnection(
                _multiplexedClientTransport.CreateConnection(
                    serverAddress,
                    _multiplexedConnectionOptions,
                    _clientAuthenticationOptions),
                _connectionOptions);
}
