// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a duplex transport.</summary>
public interface IDuplexConnection : ITransportConnection
{
    /// <summary>Reads data from the connection.</summary>
    /// <param name="buffer">The buffer that holds the read data.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The number of bytes read. The implementation should always return a positive and non-null number of
    /// bytes. If it can't read bytes, it should throw <see cref="TransportException" />.</returns>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Shuts down the connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    Task ShutdownAsync(CancellationToken cancellationToken);

    /// <summary>Writes data over the connection.</summary>
    /// <param name="buffers">The buffers containing the data to write.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes once the buffers are written.</returns>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken);
}
