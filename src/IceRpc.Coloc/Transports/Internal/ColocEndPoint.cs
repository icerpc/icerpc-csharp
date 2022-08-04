// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal class ColocEndPoint : System.Net.EndPoint
{
    private readonly ServerAddress _serverAddress;

    /// <inheritdoc/>
    public override AddressFamily AddressFamily => AddressFamily.Unspecified;

    /// <inheritdoc/>
    public override string ToString() => _serverAddress.ToString();

    internal ColocEndPoint(ServerAddress serverAddress) => _serverAddress = serverAddress;
}
