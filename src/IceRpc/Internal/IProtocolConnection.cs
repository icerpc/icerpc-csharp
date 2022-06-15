// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A protocol connection enables communication over a network connection using either the ice or icerpc
/// protocol.</summary>
internal interface IProtocolConnection
{
    /// <summary>Gets the protocol implemented by this protocol connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Aborts the connection.</summary>
    /// <param name="exception">The exception that caused the abort. Pending invocations will throw this exception.
    /// </param>
    void Abort(Exception exception);

    /// <summary>Accepts requests and returns once the connection is closed or the shutdown completes.</summary>
    /// <param name="connection">The connection of incoming requests created by this method.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task AcceptRequestsAsync(IConnection connection);

    /// <summary>Connects a protocol connection.</summary>
    /// <param name="isServer"><c>true</c> if the connection is a server connection, <c>false</c> otherwise.</param>
    /// <param name="onIdle">The callback called by the protocol connection when the connection is idle.</param>
    /// <param name="onShutdown">The callback called by the protocol connection to initiate shutdown.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The network connection information.</returns>
    Task<NetworkConnectionInformation> ConnectAsync(
        bool isServer,
        Action onIdle,
        Action<string> onShutdown,
        CancellationToken cancel);

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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task ShutdownAsync(string message, CancellationToken cancel = default);
}
