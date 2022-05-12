// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection, Configure.IceOptions>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            IDispatcher dispatcher,
            bool isServer,
            Configure.IceOptions? protocolOptions,
            CancellationToken cancel)
        {
            var protocolConnection = new IceProtocolConnection(networkConnection, dispatcher, protocolOptions);

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
