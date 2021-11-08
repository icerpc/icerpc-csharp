// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>A protocol connection enables communication over a network connection using either the Ice1
    /// or Ice2 protocol.</summary>
    internal interface IProtocolConnection : IDisposable
    {
        /// <summary>Returns <c>true</c> if one or more dispatches are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasDispatchesInProgress { get; }

        /// <summary>Returns <c>true</c> if one or more invocations are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasInvocationsInProgress { get; }

        /// <summary>The <see cref="Connection.ShutdownAsync(CancellationToken)"/> call has been canceled. The protocol
        /// implementation should take action to speed-up the shutdown when notified of the cancellation by this
        /// method.</summary>
        void ShutdownCanceled();

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task PingAsync(CancellationToken cancel);

        /// <summary>Receives a request.</summary>
        /// <returns>The incoming request.</returns>
        Task<IncomingRequest> ReceiveRequestAsync();

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
        /// <param name="message">The reason of the connection shutdown.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(string message, CancellationToken cancel);
    }

    /// <summary>Creates a protocol connection from a network connection.</summary>
    internal delegate Task<(IProtocolConnection, NetworkConnectionInformation)> ProtocolConnectionFactory<T>(
        T networkConnection,
        int incomingFrameMaxSize,
        bool isServer,
        CancellationToken cancel);
}
