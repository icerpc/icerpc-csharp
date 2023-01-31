// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IConnectionContext" /> using a protocol connection.</summary>
internal sealed class ConnectionContext : IConnectionContext
{
    public IInvoker Invoker => _protocolConnection;

    public TransportConnectionInformation TransportConnectionInformation { get; }

    private readonly IProtocolConnection _protocolConnection;

    internal ConnectionContext(
        IProtocolConnection protocolConnection,
        TransportConnectionInformation transportConnectionInformation)
    {
        _protocolConnection = protocolConnection;
        TransportConnectionInformation = transportConnectionInformation;
    }
}
