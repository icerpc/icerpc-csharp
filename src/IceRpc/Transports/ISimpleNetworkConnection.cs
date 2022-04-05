// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a simple transport. The IceRPC core calls <see
    /// cref="INetworkConnection.ConnectAsync"/> before calling other methods.</summary>
    public interface ISimpleNetworkConnection : INetworkConnection
    {
        /// <summary>Reads data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes read. The implemention should always return a positive and non-null number of
        /// bytes. If it can't read bytes, it should throw <see cref="ConnectionLostException"/>.</returns>
        /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Writes data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to write.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are written.</returns>
        /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
        ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}
