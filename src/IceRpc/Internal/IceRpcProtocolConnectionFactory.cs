// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an icerpc protocol connection from a multiplexed network connection.</summary>
    internal class IceRpcProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateConnectionAsync(
            IMultiplexedNetworkConnection networkConnection,
            NetworkConnectionInformation networkConnectionInformation,
            bool isServer,
            ConnectionOptions connectionOptions,
            CancellationToken cancel)
        {
            var protocolConnection = new IceRpcProtocolConnection(networkConnection, connectionOptions);
            try
            {
                await protocolConnection.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch
            {
                protocolConnection.Dispose();
                throw;
            }
            return protocolConnection;
        }
    }
}
