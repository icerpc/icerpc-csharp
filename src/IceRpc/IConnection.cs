// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection used to send and receive requests and responses.</summary>
public interface IConnection
{
    /// <summary>Gets a value indicating whether a call to <see cref="InvokeAsync"/> can succeed after a preceding call
    /// throws <see cref="ConnectionAbortedException"/>, <see cref="ConnectionClosedException"/>,
    /// <see cref="ConnectionLostException"/><see cref="ConnectFailedException"/> or <see cref="TimeoutException"/>.
    /// </summary>
    /// <value><c>true</c> when a call to <see cref="InvokeAsync"/> can succeed after such an exception; <c>false</c>
    /// when a new call to <see cref="InvokeAsync"/> will fail.</value>
    bool IsResumable { get; }

    /// <summary>Gets the network connection information or <c>null</c> if the connection is not connected.
    /// </summary>
    NetworkConnectionInformation? NetworkConnectionInformation { get; }

    /// <summary>Gets the protocol of this connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The corresponding <see cref="IncomingResponse"/>.</returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if cancellation was requested through the cancellation
    /// token.</exception>
    /// <exception cref="TimeoutException">Thrown if the connection establishment timed out.</exception>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel);

    /// <summary>Adds a callback that will be executed when the closure of this connection is initiated. The closure of
    /// a connection can be initiated by a local call to Abort or ShutdownAsync, by the shutdown of the remote peer, or
    /// by a transport error. If the connection is already shutting down or closed, this callback is executed
    /// synchronously with this connection and an instance of <see cref="ConnectionClosedException"/>.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    void OnClose(Action<Exception> callback);
}
