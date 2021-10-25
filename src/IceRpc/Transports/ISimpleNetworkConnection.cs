// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a simple transport.</summary>
    public interface ISimpleNetworkConnection : INetworkConnection
    {
        /// <summary>Connects this network connection and returns a simple stream for communications over this network
        /// connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="ISimpleStream"/> and <see cref="NetworkConnectionInformation"/>.</returns>
        Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel);
    }
}
