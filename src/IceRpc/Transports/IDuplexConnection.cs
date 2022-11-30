// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a duplex transport.</summary>
public interface IDuplexConnection : IDisposable
{
    /// <summary>Gets the server address of this connection. The Transport property of this server address is always
    /// non-null.</summary>
    ServerAddress ServerAddress { get; }

    /// <summary>Connects this connection. This method is only called once and always before any other methods of this
    /// interface.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The <see cref="TransportConnectionInformation" />.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <remarks>A transport implementation might raise other exceptions. A connection supporting SSL can for instance
    /// raise <see cref="AuthenticationException" /> if the authentication fails while the connection is being
    /// established.</remarks>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Reads data from the connection. This method is always called after a successful <see
    /// cref="ConnectAsync" /> call. It's never called concurrently or after a <see cref="IDisposable.Dispose" />
    /// call.</summary>
    /// <param name="buffer">The buffer that holds the read data.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The number of bytes read. The implementation should always return a positive and non-null number of
    /// bytes. If it can't read bytes, it should throw <see cref="TransportException" />.</returns>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Shuts down the connection. This method is never called twice or while a <see cref="WriteAsync" /> call
    /// is in progress.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    Task ShutdownAsync(CancellationToken cancellationToken);

    /// <summary>Writes data over the connection. This method is always called after a successful <see
    /// cref="ConnectAsync" /> call. It's never called concurrently or after a <see cref="ShutdownAsync" /> or <see
    /// cref="IDisposable.Dispose" /> call.</summary>
    /// <param name="buffers">The buffers containing the data to write.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes once the buffers are written.</returns>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken);
}
