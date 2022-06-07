// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal interface IProtocolConnectionFactory<T>
    where T : INetworkConnection
{
    /// <summary>Creates a protocol connection over a network connection.</summary>
    /// <param name="networkConnection">The network connection.</param>
    /// <param name="connectionOptions">The options class to configure the connection.</param>
    /// <returns>The protocol connection.</returns>
    IProtocolConnection CreateConnection(T networkConnection, ConnectionOptions connectionOptions);
}
