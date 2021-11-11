// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice1 protocol connection from a simple network connection.</summary>
    internal class Ice1ProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        async Task<(IProtocolConnection, NetworkConnectionInformation)> IProtocolConnectionFactory<ISimpleNetworkConnection>.CreateProtocolConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            (ISimpleStream simpleStream, NetworkConnectionInformation connectionInfo) =
                await networkConnection.ConnectAsync(cancel).ConfigureAwait(false);

            // Check if we're using the special udp transport for ice1
            bool isUdp = connectionInfo.LocalEndpoint.Transport == TransportNames.Udp;
            if (isUdp)
            {
                incomingFrameMaxSize = Math.Min(incomingFrameMaxSize, UdpUtils.MaxPacketSize);
            }

            var protocolConnection = new Ice1ProtocolConnection(simpleStream, incomingFrameMaxSize, isUdp);
            try
            {
                await protocolConnection.InitializeAsync(isServer, cancel).ConfigureAwait(false);
            }
            catch
            {
                protocolConnection.Dispose();
                throw;
            }
            return (protocolConnection, connectionInfo);
        }
    }
}
