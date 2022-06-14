// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>Creates an ice protocol connection from a simple network connection.</summary>
    internal class IceProtocolConnectionFactory : IProtocolConnectionFactory<ISimpleNetworkConnection>
    {
        public async Task<(IProtocolConnection, NetworkConnectionInformation)> CreateConnectionAsync(
            ISimpleNetworkConnection networkConnection,
            bool isServer,
            ConnectionOptions connectionOptions,
            Action onIdle,
            Action<string> onShutdown,
            CancellationToken cancel)
        {
            var protocolConnection = new IceProtocolConnection(
                networkConnection,
                connectionOptions,
                onIdle,
                onShutdown);
            try
            {
                NetworkConnectionInformation networkConnectionInformation = await protocolConnection.ConnectAsync(
                    isServer,
                    cancel).ConfigureAwait(false);
                return (protocolConnection, networkConnectionInformation);
            }
            catch
            {
                protocolConnection.Abort(new ConnectionClosedException());
                throw;
            }
        }
    }
}
