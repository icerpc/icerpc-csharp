// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal interface IProtocolConnectionFactory<T>
    where T : INetworkConnection
{
    /// <summary>Creates a protocol connection over a connected network connection.</summary>
    /// <param name="networkConnection">The network connection.</param>
    /// <param name="networkConnectionInformation">The network connection information.</param>
    /// <param name="isServer">Whether to create a server or client connection.</param>
    /// <param name="connectionOptions">The options class to configure the connection.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The protocol connection.</returns>
    Task<IProtocolConnection> CreateProtocolConnectionAsync(
        T networkConnection,
        NetworkConnectionInformation networkConnectionInformation,
        bool isServer,
        ConnectionOptions connectionOptions,
        CancellationToken cancel);
}
