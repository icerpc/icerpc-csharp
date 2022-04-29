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
            Action<Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>>? onConnect,
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
                onConnect?.Invoke(new Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>());
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
