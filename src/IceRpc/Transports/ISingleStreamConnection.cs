// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A single-stream connection enables byte data exchange over a single stream.</summary>
    public interface ISingleStreamConnection
    {
        /// <summary>Receives data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes received.</returns>
        ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are sent.</returns>
        ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}
