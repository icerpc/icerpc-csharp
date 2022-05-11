// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an icerpc protocol connection from a multiplexed network connection.</summary>
    internal class IceRpcProtocolConnectionFactory : IProtocolConnectionFactory<IMultiplexedNetworkConnection, Configure.IceRpcOptions>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            IMultiplexedNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            IDispatcher dispatcher,
            bool _,
            Configure.IceRpcOptions? protocolOptions,
            CancellationToken cancel)
        {
            var protocolConnection = new IceRpcProtocolConnection(
                networkConnection,
                dispatcher,
                protocolOptions);

            try
            {
                await protocolConnection.ConnectAsync(cancel).ConfigureAwait(false);
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
