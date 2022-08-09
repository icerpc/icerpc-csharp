// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IConnectionContext"/> using a protocol connection.</summary>
internal sealed class ConnectionContext : IConnectionContext
{
    public IInvoker Invoker => _protocolConnection;

    public TransportConnectionInformation TransportConnectionInformation { get; }

    public Protocol Protocol => _protocolConnection.ServerAddress.Protocol;

    public void OnAbort(Action<Exception> callback) => _protocolConnection.OnAbort(callback);

    public void OnShutdown(Action<string> callback) => _protocolConnection.OnShutdown(callback);

    private readonly ProtocolConnection _protocolConnection;

    internal ConnectionContext(
        ProtocolConnection protocolConnection,
        TransportConnectionInformation transportConnectionInformation)
    {
        _protocolConnection = protocolConnection;
        TransportConnectionInformation = transportConnectionInformation;
    }
}
