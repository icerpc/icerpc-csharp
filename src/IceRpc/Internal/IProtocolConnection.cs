// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Internal
{
    /// <summary>A protocol connection enables communication over a network connection using either the ice or icerpc
    /// protocol.</summary>
    internal interface IProtocolConnection : IAsyncDisposable
    {
        /// <summary>Returns <c>true</c> if one or more dispatches are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasDispatchesInProgress { get; }

        /// <summary>Returns <c>true</c> if one or more invocations are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasInvocationsInProgress { get; }

        /// <summary>The time elapsed since the last activity of the connection.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>This event is raised when the protocol connection is notified of the peer shutdown.</summary>
        Action<string>? PeerShutdownInitiated { get; set; }

        /// <summary>Aborts the connection.</summary>
        /// <param name="exception">The exception that caused the abort. Pending invocations will raise this
        /// exception.</param>
        void Abort(Exception exception);

        /// <summary>Accepts requests and returns once the connection is closed or the shutdown completes.</summary>
        /// <param name="connection">The connection of incoming requests created by this method.</param>
        Task AcceptRequestsAsync(Connection connection);

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
            Connection connection,
            CancellationToken cancel = default);

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task PingAsync(CancellationToken cancel = default);

        /// <summary>Shuts down gracefully the connection.</summary>
        /// <param name="message">The reason of the connection shutdown.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(string message, CancellationToken cancel = default);
    }
}
