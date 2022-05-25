// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection used to send and receive requests and responses.</summary>
public interface IConnection
{
    /// <summary>The connection's endpoint. For a client connection this is the connection's remote endpoint,
    /// for a server connection it's the server's endpoint.</summary>
    // TODO: can we/should we eliminate this property?
    Endpoint Endpoint { get; }

    /// <summary>Checks whether a call to <see cref="InvokeAsync"/> can succeed.</summary>
    /// <value><c>true</c> when a call to <see cref="InvokeAsync"/> can succeed. <c>false</c> when a call to
    /// <see cref="InvokeAsync"/> is guaranteed to fail, for example because the connection is closed or shutting down.
    /// </value>
    bool IsInvocable { get; }

    /// <summary>Returns the network connection information or <c>null</c> if the connection is not connected.
    /// </summary>
    NetworkConnectionInformation? NetworkConnectionInformation { get; }

    /// <summary>Returns the protocol of this connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The corresponding <see cref="IncomingResponse"/>.</returns>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel);
}
