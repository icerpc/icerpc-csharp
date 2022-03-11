// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            IDictionary<ConnectionFieldKey, OutgoingFieldValue> localFields,
            bool isServer,
            CancellationToken cancel)
        {
            // Check if we're using the special udp transport for ice
            bool isUdp = connectionInfo.LocalEndpoint.Params.TryGetValue("transport", out string? transport) &&
                transport == TransportNames.Udp;

            var protocolConnection = new IceProtocolConnection(networkConnection, isUdp);
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
