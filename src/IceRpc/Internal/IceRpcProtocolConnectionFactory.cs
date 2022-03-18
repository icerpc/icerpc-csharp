// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an icerpc protocol connection from a multiplexed network connection.</summary>
    internal class IceRpcProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            IMultiplexedNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            Configure.IceProtocolOptions? iceProtocolOptions,
            IDictionary<ConnectionFieldKey, OutgoingFieldValue> localFields,
            bool _,
            CancellationToken cancel)
        {
            var protocolConnection = new IceRpcProtocolConnection(networkConnection, localFields);
            try
            {
                await protocolConnection.InitializeAsync(cancel).ConfigureAwait(false);
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
