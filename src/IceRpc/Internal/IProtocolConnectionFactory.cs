// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal interface IProtocolConnectionFactory<T>
    where T : INetworkConnection
{
    /// <summary>Creates a protocol connection over a network connection.</summary>
    /// <param name="networkConnection">The network connection.</param>
    /// <param name="isServer"><c>true</c> if the connection is a server connection, <c>false</c> otherwise.</param>
    /// <param name="connectionOptions">The options class to configure the connection.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The protocol connection and network connection information.</returns>
    Task<(IProtocolConnection, NetworkConnectionInformation)> CreateConnectionAsync(
        T networkConnection,
        bool isServer,
        ConnectionOptions connectionOptions,
        CancellationToken cancel);
}
