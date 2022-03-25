// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Internal
{
    /// <summary>A protocol connection enables communication over a network connection using either the Ice or
    /// IceRPC protocol.</summary>
    internal interface IProtocolConnection : IDisposable
    {
        /// <summary>Returns <c>true</c> if one or more dispatches are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasDispatchesInProgress { get; }

        /// <summary>Returns <c>true</c> if one or more invocations are in progress, <c>false</c>
        /// otherwise.</summary>
        bool HasInvocationsInProgress { get; }

        /// <summary>Returns the fields provided by the peer.</summary>
        ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> PeerFields { get; }

        /// <summary>This event is raised when the protocol connection is notified of the peer shutdown.</summary>
        event Action<string>? PeerShutdownInitiated;

        /// <summary>Accepts requests and returns once the connection is closed or the shutdown completes.</summary>
        /// <param name="connection">The connection owning this protocol connection. This is used to assign <see
        /// cref="IncomingFrame.Connection"/>.</param>
        /// <param name="dispatcher">The dispatcher to dispatch the requests.</param>
        Task AcceptRequestsAsync(Connection connection, IDispatcher dispatcher);

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task PingAsync(CancellationToken cancel);

        /// <summary>Sends a request and returns the response. The implementation must complete the request payload
        /// sources and sink.</summary>
        /// <param name="connection">The connection owning this protocol connection. This is used to assign <see
        /// cref="IncomingFrame.Connection"/>.</param>
        /// <param name="request">The outgoing request to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The received response.</returns>
        Task<IncomingResponse> SendRequestAsync(
            Connection connection,
            OutgoingRequest request,
            CancellationToken cancel);

        /// <summary>Shutdowns gracefully the connection.</summary>
        /// <param name="message">The reason of the connection shutdown.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(string message, CancellationToken cancel);
    }
}
