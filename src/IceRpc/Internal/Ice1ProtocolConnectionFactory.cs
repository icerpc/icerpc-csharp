// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice1 protocol connection from a simple network connection.</summary>
    internal class Ice1ProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            // Check if we're using the special udp transport for ice1
            bool isUdp = connectionInfo.LocalEndpoint.Transport == TransportNames.Udp;
            if (isUdp)
            {
                incomingFrameMaxSize = Math.Min(incomingFrameMaxSize, UdpUtils.MaxPacketSize);
            }

            var protocolConnection = new Ice1ProtocolConnection(networkConnection, incomingFrameMaxSize, isUdp);
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
