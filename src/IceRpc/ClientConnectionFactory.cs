// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Net.Security;

namespace IceRpc;

/// <summary>The default implementation of <see cref="IClientConnectionFactory" />.</summary>
public sealed class ClientConnectionFactory : IClientConnectionFactory
{
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
    /// <see cref="IDuplexClientTransport.Default" />.</param>
    /// <param name="multiplexedClientTransport">The multiplexed client transport. Null is equivalent to
    /// <see cref="IMultiplexedClientTransport.Default" />.</param>
    public ClientConnectionFactory(
        ConnectionOptions connectionOptions,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null)
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
        // which is accepted locally is the peer remote control stream.
        _multiplexedConnectionOptions = new MultiplexedConnectionOptions
        {
            MaxBidirectionalStreams = connectionOptions.Dispatcher is null ? 0 :
                connectionOptions.MaxIceRpcBidirectionalStreams,

            // Add an additional stream for the icerpc protocol control stream.
            MaxUnidirectionalStreams = connectionOptions.Dispatcher is null ? 1 :
                connectionOptions.MaxIceRpcUnidirectionalStreams + 1,

            Pool = connectionOptions.Pool,
            MinSegmentSize = connectionOptions.MinSegmentSize,
            PayloadErrorCodeConverter = IceRpcProtocol.Instance.PayloadErrorCodeConverter
        };
    }

    /// <summary>Creates a protocol connection to the specified server address.</summary>
    /// <param name="serverAddress">The address of the server.</param>
    /// <returns>The new protocol connection.</returns>
    /// <remarks>The protocol connection returned by this factory method is not connected. The caller must call
    /// <see cref="IProtocolConnection.ConnectAsync" /> exactly once on this connection before calling
    /// <see cref="IInvoker.InvokeAsync" />.</remarks>
    public IClientConnection CreateConnection(ServerAddress serverAddress) =>
        serverAddress.Protocol == Protocol.Ice ?
            new CoreClientConnection<IDuplexConnection>(
                _duplexClientTransport.CreateConnection(
                    serverAddress,
                    _duplexConnectionOptions,
                    _clientAuthenticationOptions),
                (transportConnection, transportConnectionInformation) =>
                    new IceProtocolConnection(
                        transportConnection,
                        transportConnectionInformation,
                        isServer: false,
                        _connectionOptions)) :
            new CoreClientConnection<IMultiplexedConnection>(
                _multiplexedClientTransport.CreateConnection(
                    serverAddress,
                    _multiplexedConnectionOptions,
                    _clientAuthenticationOptions),
                (transportConnection, transportConnectionInformation) =>
                    new IceRpcProtocolConnection(
                        transportConnection,
                        transportConnectionInformation,
                        isServer: false,
                        _connectionOptions));

    /// <summary>A client connection which is a small wrapper for a protocol connection. It takes care of the connection
    /// establishment logic for the transport and protocol connections.</summary>
    private sealed class CoreClientConnection<T> : IClientConnection where T : ITransportConnection
    {
        public ServerAddress ServerAddress => _transportConnection.ServerAddress;

        public Task ShutdownComplete =>
            _protocolConnection?.ShutdownComplete ??
            throw new InvalidOperationException("cannot get ShutdownComplete before calling ConnectAsync");

        private readonly T _transportConnection;
        private readonly Func<T, TransportConnectionInformation, IProtocolConnection> _protocolConnectionFactory;
        private IProtocolConnection? _protocolConnection;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            // Connect the transport connection
            TransportConnectionInformation transportConnectionInformation;
            try
            {
                transportConnectionInformation = await _transportConnection.ConnectAsync(
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TransportException exception) when (
                exception.ApplicationErrorCode is ulong errorCode &&
                errorCode == (ulong)ConnectionErrorCode.ConnectRefused)
            {
                throw new ConnectionException(ConnectionErrorCode.ConnectRefused);
            }

            _protocolConnection = _protocolConnectionFactory(_transportConnection, transportConnectionInformation);

            return transportConnectionInformation;
        }

        public async ValueTask DisposeAsync()
        {
            await _transportConnection.DisposeAsync().ConfigureAwait(false);
            if (_protocolConnection is not null)
            {
                await _protocolConnection.DisposeAsync().ConfigureAwait(false);
            }
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _protocolConnection?.InvokeAsync(request, cancellationToken) ??
            throw new InvalidOperationException("cannot call InvokeAsync before calling ConnectAsync");

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        internal CoreClientConnection(
            T transportConnection,
            Func<T, TransportConnectionInformation, IProtocolConnection> protocolConnectionFactory)
        {
            _transportConnection = transportConnection;
            _protocolConnectionFactory = protocolConnectionFactory;
        }
    }
}
