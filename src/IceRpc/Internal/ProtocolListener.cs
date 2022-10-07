// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IListener{T}" /> for protocol connections.</summary>
/// <typeparam name="T">The transport connection type.</typeparam>
internal abstract class ProtocolListener<T> : IListener<IProtocolConnection>
{
    public ServerAddress ServerAddress => _transportListener.ServerAddress;

    private readonly IListener<T> _transportListener;

    public async Task<(IProtocolConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
        CancellationToken cancellationToken)
    {
        (T transportConnection, EndPoint remoteNetworkAddress) = await _transportListener.AcceptAsync(cancellationToken)
            .ConfigureAwait(false);
        return (CreateProtocolConnection(transportConnection), remoteNetworkAddress);
    }

    public ValueTask DisposeAsync() => _transportListener.DisposeAsync();

    internal ProtocolListener(IListener<T> transportListener) => _transportListener = transportListener;

    private protected abstract IProtocolConnection CreateProtocolConnection(T transportConnection);
}

internal sealed class IceProtocolListener : ProtocolListener<IDuplexConnection>
{
    private readonly ConnectionOptions _connectionOptions;

    internal IceProtocolListener(
        ConnectionOptions connectionOptions,
        IListener<IDuplexConnection> listener)
        : base(listener) => _connectionOptions = connectionOptions;

    private protected override IProtocolConnection CreateProtocolConnection(IDuplexConnection duplexConnection) =>
        new IceProtocolConnection(duplexConnection, isServer: true, _connectionOptions);
}

internal sealed class IceRpcProtocolListener : ProtocolListener<IMultiplexedConnection>
{
    private readonly ConnectionOptions _connectionOptions;

    internal IceRpcProtocolListener(
        ConnectionOptions connectionOptions,
        IListener<IMultiplexedConnection> listener)
        : base(listener) => _connectionOptions = connectionOptions;

    private protected override IProtocolConnection CreateProtocolConnection(
        IMultiplexedConnection multiplexedConnection) =>
        new IceRpcProtocolConnection(multiplexedConnection, isServer: true, _connectionOptions);
}
