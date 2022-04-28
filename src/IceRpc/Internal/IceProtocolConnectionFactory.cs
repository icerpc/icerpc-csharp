// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public async Task<IProtocolConnection> CreateProtocolConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            NetworkConnectionInformation connectionInfo,
            Configure.ConnectionOptions connectionOptions,
            FeatureCollection features,
            bool isServer,
            CancellationToken cancel)
        {
            var protocolConnection = new IceProtocolConnection(
                connectionOptions.Dispatcher,
                networkConnection,
                connectionOptions.IceProtocolOptions ?? Configure.IceProtocolOptions.Default);

            try
            {
                await protocolConnection.InitializeAsync(isServer, cancel).ConfigureAwait(false);
                connectionOptions.OnConnect?.Invoke(
                        new Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>(),
                        features);
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
