// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IListener{T}"/> for protocol connections.</summary>
/// <typeparam name="T">The transport connection type.</typeparam>
internal abstract class ProtocolListener<T> : IListener<IProtocolConnection>
{
    public ServerAddress ServerAddress => _transportListener.ServerAddress;

    private readonly IListener<T> _transportListener;

    public async Task<(IProtocolConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync()
    {
        (T transportConnection, EndPoint remoteNetworkAddress) = await _transportListener.AcceptAsync()
            .ConfigureAwait(false);

        return (CreateProtocolConnection(transportConnection), remoteNetworkAddress);
    }

    public void Dispose() => _transportListener.Dispose();

    internal ProtocolListener(IListener<T> transportListener) => _transportListener = transportListener;

    private protected abstract IProtocolConnection CreateProtocolConnection(T transportConnection);
}

internal sealed class IceProtocolListener : ProtocolListener<IDuplexConnection>
{
    private readonly ConnectionOptions _connectionOptions;

    internal IceProtocolListener(
        ServerAddress serverAddress,
        ServerOptions serverOptions,
        IDuplexServerTransport duplexServerTransport)
        : base(duplexServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions
            {
                MinSegmentSize = serverOptions.ConnectionOptions.MinSegmentSize,
                Pool = serverOptions.ConnectionOptions.Pool,
            },
            serverOptions.ServerAuthenticationOptions)) =>
        _connectionOptions = serverOptions.ConnectionOptions;

    private protected override IProtocolConnection CreateProtocolConnection(IDuplexConnection duplexConnection) =>
        new IceProtocolConnection(duplexConnection, isServer: true, _connectionOptions);
}

internal sealed class IceRpcProtocolListener : ProtocolListener<IMultiplexedConnection>
{
    private readonly ConnectionOptions _connectionOptions;

    internal IceRpcProtocolListener(
        ServerAddress serverAddress,
        ServerOptions serverOptions,
        IMultiplexedServerTransport multiplexedServerTransport)
        : base(multiplexedServerTransport.Listen(
            serverAddress,
            new MultiplexedConnectionOptions
            {
                MaxBidirectionalStreams = serverOptions.ConnectionOptions.MaxIceRpcBidirectionalStreams,
                // Add an additional stream for the icerpc protocol control stream.
                MaxUnidirectionalStreams = serverOptions.ConnectionOptions.MaxIceRpcUnidirectionalStreams + 1,
                MinSegmentSize = serverOptions.ConnectionOptions.MinSegmentSize,
                Pool = serverOptions.ConnectionOptions.Pool,
                StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter
            },
            serverOptions.ServerAuthenticationOptions)) =>
        _connectionOptions = serverOptions.ConnectionOptions;

    private protected override IProtocolConnection CreateProtocolConnection(
        IMultiplexedConnection multiplexedConnection) =>
        new IceRpcProtocolConnection(multiplexedConnection, isServer: true, _connectionOptions);
}
