// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a multiplexed transport.</summary>
    public interface IMultiplexedNetworkConnection : INetworkConnection
    {
        /// <summary>Connects this network connection and returns a multiplexed stream factory.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="IMultiplexedStreamFactory"/> and <see cref="NetworkConnectionInformation"/>.
        /// </returns>
        Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel);
    }
}
