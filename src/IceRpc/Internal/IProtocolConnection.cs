// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>A protocol connection represents a layer 7 network connection in the OSI model. It enables
    /// communications using an Ice protocol (Ice1, Ice2 or Coloc).</summary>
    public interface IProtocolConnection : IDisposable
    {
        /// <summary>Returns <c>true</c> if dispatch are in progress, <c>false</c> otherwise.</summary>
        bool HasDispatchInProgress { get; }

        /// <summary>Returns <c>true</c> if invocations are in progress, <c>false</c> otherwise.</summary>
        bool HasInvocationsInProgress { get; }

        /// <summary>Gets or set the idle timeout.</summary>
        TimeSpan IdleTimeout { get; }

        /// <summary>The time of the connection's last activity.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>Initializes the connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task InitializeAsync(CancellationToken cancel);

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task PingAsync(CancellationToken cancel);

        /// <summary>Receives a request.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The incoming request or null if the connection has been gracefully shutdown.</returns>
        Task<IncomingRequest?> ReceiveRequestAsync(CancellationToken cancel);

        /// <summary>Receives a response for a given request.</summary>
        /// <param name="request">The outgoing request associated to the response to receive.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The incoming response.</returns>
        Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel);

        /// <summary>Sends a request.</summary>
        /// <param name="request">The outgoing request to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel);

        /// <summary>Sends a request.</summary>
        /// <param name="request">The incoming request associated to the response to send.</param>
        /// <param name="response">The outgoing response to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task SendResponseAsync(IncomingRequest request, OutgoingResponse response, CancellationToken cancel);

        /// <summary>Shutdowns gracefully the connection.</summary>
        /// <param name="closedByPeer"><c>true</c> if the shutdown is the result of the peer shutting down the
        /// connection, <c>false</c> otherwise.</param>
        /// <param name="message">The reason of the connection shutdown.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(bool closedByPeer, string message, CancellationToken cancel);

        /// <summary>Cancel the shutdown which is progress.</summary>
        void CancelShutdown();

        /// <summary>Waits for graceful shutdown of the connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The reason why the peer shutdown the connection.</returns>
        Task<string> WaitForShutdownAsync(CancellationToken cancel);
    }
}
