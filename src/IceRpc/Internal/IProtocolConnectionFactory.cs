// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    internal interface IProtocolConnectionFactory<T> where T : INetworkConnection
    {
        /// <summary>Creates a protocol connection from a network connection.</summary>
        Task<IProtocolConnection> CreateProtocolConnectionAsync(
            T networkConnection,
            NetworkConnectionInformation connectionInformation,
            Connection connection,
            Configure.ConnectionOptions connectionOptions,
            bool isServer,
            CancellationToken cancel);
    }
}
