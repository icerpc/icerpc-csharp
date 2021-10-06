// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>A protocol connection enables communication over a network connection using either the Ice1
    /// or Ice2 protocol.</summary>
    internal interface IProtocolConnection : IDisposable
    {
        /// <summary>Returns <c>true</c> if one or more dispatch are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasDispatchInProgress { get; }

        /// <summary>Returns <c>true</c> if one or more invocations are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasInvocationsInProgress { get; }

        /// <summary>Cancel the shutdown which is in progress.</summary>
        void CancelShutdown();

        /// <summary>Initializes the connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task InitializeAsync(CancellationToken cancel);

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task PingAsync(CancellationToken cancel);

        /// <summary>Receives a request.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The incoming request or null if the connection is shutdown.</returns>
        Task<IncomingRequest> ReceiveRequestAsync(CancellationToken cancel);

        /// <summary>Receives a response for a given request.</summary>
        /// <param name="request">The outgoing request associated to the response to receive.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The incoming response.</returns>
        Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel);

        /// <summary>Sends a request.</summary>
        /// <param name="request">The outgoing request to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel);

        /// <summary>Sends a response.</summary>
        /// <param name="response">The outgoing response to send.</param>
        /// <param name="request">The incoming request associated to the response to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task SendResponseAsync(OutgoingResponse response, IncomingRequest request, CancellationToken cancel);

        /// <summary>Shutdowns gracefully the connection.</summary>
        /// <param name="shutdownByPeer"><c>true</c> if the shutdown is from the peer, <c>false</c> otherwise.</param>
        /// <param name="message">The reason of the connection shutdown.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(bool shutdownByPeer, string message, CancellationToken cancel);

        /// <summary>Waits for graceful shutdown of the connection by the peer.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The reason of the peer shutdown.</returns>
        Task<string> WaitForShutdownAsync(CancellationToken cancel);
    }
}
