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
            Configure.ConnectionOptions connectionOptions,
            FeatureCollection features,
            bool _,
            CancellationToken cancel)
        {
            var protocolConnection = new IceRpcProtocolConnection(
                connectionOptions.Dispatcher,
                networkConnection,
                connectionOptions.Fields,
                connectionOptions.OnConnect == null ? null : fields => connectionOptions.OnConnect(fields, features));

            try
            {
                await protocolConnection.InitializeAsync(cancel).ConfigureAwait(false);
            }
            catch
            {
                await protocolConnection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return protocolConnection;
        }
    }
}
