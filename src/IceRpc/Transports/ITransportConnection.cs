// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection.</summary>
public interface ITransportConnection : IAsyncDisposable
{
    /// <summary>Gets the server address of this connection. This server address Transport property is non-null.
    /// </summary>
    ServerAddress ServerAddress { get; }

    /// <summary>Connects this connection. This method should only be called once.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The <see cref="TransportConnectionInformation" />.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <remarks>A transport implementation might raise other exceptions. A connection supporting SSL can for instance
    /// raise <see cref="AuthenticationException" /> if the authentication fails while the connection is being
    /// established.</remarks>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);
}
