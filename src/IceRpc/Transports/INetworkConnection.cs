// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>A network connection represents a transport-level connection used to exchange data as bytes.  The
/// IceRPC core calls <see cref="ConnectAsync"/> before calling other methods.</summary>
public interface INetworkConnection
{
    /// <summary>Gets the endpoint of this connection. This endpoint always includes a transport parameter that
    /// identifies the underlying transport.</summary>
    Endpoint Endpoint { get; }

    /// <summary>Connects this network connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The <see cref="NetworkConnectionInformation"/>.</returns>
    /// <exception cref="ConnectFailedException">Thrown if the connection establishment to the per
    /// failed.</exception>
    /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection while the
    /// connection is being established.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    /// <exception cref="TransportException">Thrown if an unexpected error was encountered.</exception>
    /// <remarks>A transport implementation might raise other exceptions. A network connection supporting SSL can
    /// for instance raise <see cref="AuthenticationException"/> if the authentication fails while the connection
    /// is being established.</remarks>
    Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel);
}
