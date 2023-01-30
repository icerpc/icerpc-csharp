// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Allows users of <see cref="IceRpcProtocolConnection" /> to observe exceptions thrown by tasks created by
/// <see cref="IceRpcProtocolConnection" />.</summary>
/// <remarks>The implementation must not throw any exception.</remarks>
internal interface ITaskExceptionObserver
{
    /// <summary>The dispatch started and failed to return a response.</summary>
    /// <param name="request">The incoming request that was dispatched.</param>
    /// <param name="connectionInformation">Information about the connection that received the request.</param>
    /// <param name="exception">The exception thrown by the dispatch.</param>
    void DispatchFailed(
        IncomingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception);

    /// <summary>The dispatch was refused before the incoming request could be read and decoded.</summary>
    /// <param name="connectionInformation">Information about the connection that received the request.</param>
    /// <param name="exception">The exception that caused this refusal.</param>
    void DispatchRefused(TransportConnectionInformation connectionInformation, Exception exception);

    /// <summary>The sending of the payload continuation of a request failed.</summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="connectionInformation">Information about the connection that was sending the request.</param>
    /// <param name="exception">The exception thrown when sending the payload continuation.</param>
    void RequestPayloadContinuationFailed(
        OutgoingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception);
}
