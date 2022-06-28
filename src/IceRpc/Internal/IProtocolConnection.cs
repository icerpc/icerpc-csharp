// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A protocol connection enables communication over a network connection using either the ice or icerpc
/// protocol.</summary>
internal interface IProtocolConnection : IAsyncDisposable
{
    /// <summary>Gets the protocol implemented by this protocol connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Connects the protocol connection.</summary>
    /// <param name="isServer"><c>true</c> if the connection is a server connection, <c>false</c> otherwise.</param>
    /// <param name="connection">The value for <see cref="IncomingFrame.Connection"/> in incoming requests created by
    /// this protocol connection.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The network connection information.</returns>
    /// <remarks>This method should be called only once.</remarks>
    Task<NetworkConnectionInformation> ConnectAsync(
        bool isServer,
        IConnection connection,
        CancellationToken cancel);

    /// <summary>Sends a request and returns the response. The implementation must complete the request payload and
    /// payload stream.</summary>
    /// <param name="request">The outgoing request to send.</param>
    /// <param name="connection">The value for <see cref="IncomingFrame.Connection"/> in incoming responses created by
    /// this protocol connection.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The received response.</returns>
    Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        IConnection connection,
        CancellationToken cancel = default);

    /// <summary>Adds a callback that will be executed when the closure of this connection is initiated. The closure of
    /// a connection can be initiated by a local call to Abort or ShutdownAsync, by the shutdown of the remote peer, or
    /// by a transport error. If the connection is already shutting down or closed, this callback is executed
    /// synchronously with this connection and an instance of <see cref="ConnectionClosedException"/>.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    void OnClose(Action<Exception> callback);

    /// <summary>Shuts down gracefully the connection.</summary>
    /// <param name="message">The reason of the connection shutdown.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>This method should be called only once and always after a successful <see
    /// cref="ConnectAsync"/>.</remarks>
    Task ShutdownAsync(string message, CancellationToken cancel = default);
}
