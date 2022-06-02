// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>A protocol connection enables communication over a network connection using either the ice or icerpc
    /// protocol.</summary>
    internal interface IProtocolConnection : IDisposable
    {
        /// <summary>This callback is called when the protocol connection wants to initiate shutdown.</summary>
        Action<string>? InitiateShutdown { get; set; }

        /// <summary>Returns the protocol implemented by this protocol connection.</summary>
        Protocol Protocol { get; }

        /// <summary>Aborts the connection.</summary>
        /// <param name="exception">The exception that caused the abort. Pending invocations will throw this exception.
        /// </param>
        void Abort(Exception exception);

        /// <summary>Accepts requests and returns once the connection is closed or the shutdown completes.</summary>
        /// <param name="connection">The connection of incoming requests created by this method.</param>
        Task AcceptRequestsAsync(IConnection connection);

        /// <summary>Connects the protocol connection. The implementation should also connect the underlying network
        /// connection.</summary>
        /// <param name="isServer"><c>true</c> if the protocol connection is a server-side connection.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The network connection information</returns>
        Task<NetworkConnectionInformation> ConnectAsync(bool isServer, CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with the network connection of
        /// this protocol connection. Compatible means a client could reuse the network connection instead of
        /// establishing a new network connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when the network connection is a network connection whose parameters are compatible
        /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);

        /// <summary>Sends a request and returns the response. The implementation must complete the request payload
        /// and payload stream.</summary>
        /// <param name="request">The outgoing request to send.</param>
        /// <param name="connection">The connection of incoming responses created by this method.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The received response.</returns>
        Task<IncomingResponse> InvokeAsync(
            OutgoingRequest request,
            IConnection connection,
            CancellationToken cancel = default);

        /// <summary>Shuts down gracefully the connection.</summary>
        /// <param name="message">The reason of the connection shutdown.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(string message, CancellationToken cancel = default);
    }
}
