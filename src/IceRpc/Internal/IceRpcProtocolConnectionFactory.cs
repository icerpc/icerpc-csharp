// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Internal
{
    /// <summary>Creates an icerpc protocol connection from a multiplexed network connection.</summary>
    internal class IceRpcProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            IMultiplexedNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            Configure.ConnectionOptions connectionOptions,
            Action<Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>>? onConnect,
            bool _,
            CancellationToken cancel)
        {
            var protocolConnection = new IceRpcProtocolConnection(
                connectionOptions.Dispatcher,
                networkConnection,
                connectionOptions.Fields,
                onConnect);

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
