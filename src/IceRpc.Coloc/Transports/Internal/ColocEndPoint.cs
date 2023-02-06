// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal class ColocEndPoint : System.Net.EndPoint
{
    /// <inheritdoc/>
    public override AddressFamily AddressFamily => AddressFamily.Unspecified;

    private readonly ServerAddress _serverAddress;

    /// <inheritdoc/>
    public override string ToString() => _serverAddress.ToString();

    internal ColocEndPoint(ServerAddress serverAddress) => _serverAddress = serverAddress;
}
