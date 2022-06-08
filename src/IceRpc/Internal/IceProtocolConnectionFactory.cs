// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public IProtocolConnection CreateConnection(
            ISimpleNetworkConnection networkConnection,
            ConnectionOptions connectionOptions) =>
            new IceProtocolConnection(networkConnection, connectionOptions);
    }
}
