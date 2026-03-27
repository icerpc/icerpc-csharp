// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.Coloc.Internal;

internal class ColocEndPoint : System.Net.EndPoint
{
    /// <inheritdoc/>
    public override AddressFamily AddressFamily => AddressFamily.Unspecified;

    private readonly TransportAddress _transportAddress;

    /// <inheritdoc/>
    public override string ToString() => $"{_transportAddress.Host}:{_transportAddress.Port}";

    internal ColocEndPoint(TransportAddress transportAddress) => _transportAddress = transportAddress;
}
