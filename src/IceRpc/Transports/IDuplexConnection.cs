// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a duplex transport.</summary>
public interface IDuplexConnection : IDisposable
{
    /// <summary>Gets the endpoint of this connection. The Transport property of this endpoint is always non-null.
    /// </summary>
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

    /// <summary>Reads data from the connection.</summary>
    /// <param name="buffer">The buffer that holds the read data.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The number of bytes read. The implementation should always return a positive and non-null number of
    /// bytes. If it can't read bytes, it should throw <see cref="ConnectionLostException"/>.</returns>
    /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel);

    /// <summary>Shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    Task ShutdownAsync(CancellationToken cancel);

    /// <summary>Writes data over the connection.</summary>
    /// <param name="buffers">The buffers containing the data to write.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes once the buffers are written.</returns>
    /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
}
