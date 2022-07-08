// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IConnectionContext"/> using a protocol connection.</summary>
internal sealed class ConnectionContext : IConnectionContext
{
    public IInvoker Invoker => _protocolConnection;

    public NetworkConnectionInformation NetworkConnectionInformation { get; }

    public Protocol Protocol => _protocolConnection.Protocol;

    public void OnAbort(Action<Exception> callback) => _protocolConnection.OnAbort(callback);

    public void OnShutdown(Action<string> callback) => _protocolConnection.OnShutdown(callback);

    private readonly IProtocolConnection _protocolConnection;

    internal ConnectionContext(
        IProtocolConnection protocolConnection,
        NetworkConnectionInformation networkConnectionInformation)
    {
        _protocolConnection = protocolConnection;
        NetworkConnectionInformation = networkConnectionInformation;
    }
}
