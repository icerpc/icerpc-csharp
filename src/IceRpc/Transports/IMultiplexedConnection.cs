// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;
using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a multiplexed transport.</summary>
public interface IMultiplexedConnection : IAsyncDisposable
{
    /// <summary>Gets the endpoint of this connection. This endpoint always includes a transport parameter that
    /// identifies the underlying transport.</summary>
    Endpoint Endpoint { get; }

    /// <summary>Connects this connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The <see cref="TransportConnectionInformation"/>.</returns>
    /// <exception cref="ConnectFailedException">Thrown if the connection establishment to the per failed.</exception>
    /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection while the
    /// connection is being established.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    /// <exception cref="TransportException">Thrown if an unexpected error was encountered.</exception>
    /// <remarks>A transport implementation might raise other exceptions. A connection supporting SSL can for instance
    /// raise <see cref="AuthenticationException"/> if the authentication fails while the connection is being
    /// established.</remarks>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel);

    /// <summary>Accepts a remote stream.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The remote stream.</returns>
    ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel);

    /// <summary>Creates a local stream.</summary>
    /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
    /// <returns>The local stream.</returns>
    IMultiplexedStream CreateStream(bool bidirectional);

    /// <summary>Shuts down the connection.</summary>
    /// <param name="completeException">The exception provided to the stream <see cref="IDuplexPipe"/>.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    Task ShutdownAsync(Exception completeException, CancellationToken cancel);
}
