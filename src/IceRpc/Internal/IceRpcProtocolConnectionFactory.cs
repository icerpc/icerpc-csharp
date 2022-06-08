// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an icerpc protocol connection from a multiplexed network connection.</summary>
    internal class IceRpcProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection>
    {
        public IProtocolConnection CreateConnection(
            IMultiplexedNetworkConnection networkConnection,
            ConnectionOptions connectionOptions) =>
            new IceRpcProtocolConnection(networkConnection, connectionOptions);
    }
}
