// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Authentication;

namespace IceRpc.Transports
{
    /// <summary>A network connection represents a transport-level connection used to exchange data as bytes.  The
    /// IceRPC core calls <see cref="ConnectAsync"/> before calling other methods.</summary>
    public interface INetworkConnection : IDisposable
    {
        /// <summary>Connects this network connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="NetworkConnectionInformation"/>.</returns>
        /// <exception cref="ConnectFailedException">Thrown if the connection establishment to the per
        /// failed.</exception>
        /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection while the
        /// connection is being established.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
        /// <exception cref="TransportException">Thrown if an unexpected error was encountered.</exception>
        /// <remarks>A transport implementation might raise other exceptions. A network connection supporting SSL can
        /// for instance raise <see cref="AuthenticationException"/> if the authentication fails while the connection
        /// is being established.</remarks>
        Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this network connection.
        /// Compatible means a client could reuse this network connection instead of establishing a new network
        /// connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a network connection whose parameters are compatible with the
        /// parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
