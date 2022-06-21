// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal class ColocEndPoint : System.Net.EndPoint
{
    private readonly Endpoint _endpoint;

    /// <inheritdoc/>
    public override AddressFamily AddressFamily => AddressFamily.Unspecified;

    /// <inheritdoc/>
    public override string ToString() => _endpoint.ToString();

    internal ColocEndPoint(Endpoint endpoint) => _endpoint = endpoint;
}
