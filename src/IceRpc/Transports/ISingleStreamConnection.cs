// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A single stream connection enable byte data exchange over a single stream connection.</summary>
    public interface ISingleStreamConnection
    {
        /// <summary>Receives data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes received.</returns>
        ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Receives data from the connection until the given buffer is full.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is filled up with received data.</returns>
        async ValueTask ReceiveUntilFullAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // Loop until we received enough data to fully fill the given buffer.
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = await ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException("unexpected end of stream");
                }
                offset += received;
            }
        }

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is sent.</returns>
        ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are sent.</returns>
        ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}
