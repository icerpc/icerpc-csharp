// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection used to send and receive requests and responses.</summary>
public interface IConnection
{
    /// <summary>Gets a value indicating whether a call to <see cref="InvokeAsync"/> can succeed.</summary>
    /// <value><c>true</c> when a call to <see cref="InvokeAsync"/> can succeed. <c>false</c> when a call to
    /// <see cref="InvokeAsync"/> is guaranteed to fail, for example because the connection is closed or shutting down.
    /// </value>
    bool IsInvocable { get; }

    /// <summary>Gets the network connection information or <c>null</c> if the connection is not connected.
    /// </summary>
    NetworkConnectionInformation? NetworkConnectionInformation { get; }

    /// <summary>Adds a callback that will be executed when the closure of this connection is initiated. If the
    /// connection is already closed or closing, this callback is executed after all callbacks already in progress
    /// (if any) have completed.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    void OnClose(Action<IConnection, Exception> callback);

    /// <summary>Gets the protocol of this connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The corresponding <see cref="IncomingResponse"/>.</returns>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel);
}
