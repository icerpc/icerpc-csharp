// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A simple stream enables byte data exchange over a simple network connection.</summary>
    public interface ISimpleStream
    {
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
