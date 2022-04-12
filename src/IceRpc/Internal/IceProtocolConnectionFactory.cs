// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            Configure.ConnectionOptions connectionOptions,
            bool isServer,
            CancellationToken cancel)
        {
            var protocolConnection = new IceProtocolConnection(
                networkConnection,
                connectionOptions.IceProtocolOptions ?? Configure.IceProtocolOptions.Default);

            try
            {
                await protocolConnection.InitializeAsync(isServer, cancel).ConfigureAwait(false);
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
