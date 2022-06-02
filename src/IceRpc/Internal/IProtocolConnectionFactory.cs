// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal interface IProtocolConnectionFactory<T>
    where T : INetworkConnection
{
    /// <summary>Creates a protocol connection over a network connection.</summary>
    IProtocolConnection CreateConnection(T networkConnection, ConnectionOptions connectionOptions);
}
