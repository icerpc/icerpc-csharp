// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an icerpc protocol connection from a multiplexed network connection.</summary>
    internal class IceRpcProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection>
    {
        public async Task<(IProtocolConnection, NetworkConnectionInformation)> CreateConnectionAsync(
            IMultiplexedNetworkConnection networkConnection,
            bool isServer,
            ConnectionOptions connectionOptions,
            CancellationToken cancel)
        {
            var protocolConnection = new IceRpcProtocolConnection(networkConnection, connectionOptions);
            try
            {
                NetworkConnectionInformation networkConnectionInformation = await protocolConnection.ConnectAsync(
                    cancel).ConfigureAwait(false);

                return (protocolConnection, networkConnectionInformation);
            }
            catch
            {
                protocolConnection.Dispose();
                throw;
            }
        }
    }
}
