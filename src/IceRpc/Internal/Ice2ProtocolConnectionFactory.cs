// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice2 protocol connection from a multiplexed network connection.</summary>
    internal class Ice2ProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection>
    {
        async Task<(IProtocolConnection, NetworkConnectionInformation)> IProtocolConnectionFactory<IMultiplexedNetworkConnection>.CreateProtocolConnectionAsync(
            IMultiplexedNetworkConnection networkConnection,
            int incomingFrameMaxSize,
            bool _,
            CancellationToken cancel)
        {
            (IMultiplexedStreamFactory streamFactory, NetworkConnectionInformation connectionInfo) =
                await networkConnection.ConnectAsync(cancel).ConfigureAwait(false);

            var protocolConnection = new Ice2ProtocolConnection(streamFactory, incomingFrameMaxSize);
            try
            {
                await protocolConnection.InitializeAsync(cancel).ConfigureAwait(false);
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
