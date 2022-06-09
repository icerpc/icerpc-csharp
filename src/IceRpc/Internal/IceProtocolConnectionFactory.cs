// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            NetworkConnectionInformation networkConnectionInformation,
            bool isServer,
            ConnectionOptions connectionOptions,
            CancellationToken cancel)
        {
            var protocolConnection = new IceProtocolConnection(networkConnection, connectionOptions);
            try
            {
                await protocolConnection.ConnectAsync(isServer, cancel).ConfigureAwait(false);
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
