// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal interface IProtocolConnectionFactory<T>
    where T : INetworkConnection
{
    /// <summary>Creates a protocol connection over a connected network connection.</summary>
    Task<IProtocolConnection> CreateProtocolConnectionAsync(
        T networkConnection,
        NetworkConnectionInformation connectionInformation,
        bool isServer,
        Configure.ConnectionOptions connectionOptions,
        CancellationToken cancel);
}
