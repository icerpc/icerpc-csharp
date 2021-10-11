// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A single-stream connection enables byte data exchange over a single stream.</summary>
    public interface ISingleStreamConnection
    {
        /// <summary>The maximum size of a received datagram if this connection is a datagram
        /// connection.</summary>
        int DatagramMaxReceiveSize { get; }

        /// <summary><c>true</c> for a datagram network connection; <c>false</c> otherwise.</summary>
        bool IsDatagram { get; }

        /// <summary>Reads data from the stream.</summary>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes read.</returns>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Writes data over the stream.</summary>
        /// <param name="buffers">The buffers containing the data to write.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are written.</returns>
        ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}
