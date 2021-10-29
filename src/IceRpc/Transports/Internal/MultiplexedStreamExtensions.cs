// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal static class MultiplexedStreamExtensions
    {
        /// <summary>Aborts the stream.</summary>
        /// <param name="stream">The stream to abort.</param>
        /// <param name="errorCode">The reason of the abort.</param>
        internal static void Abort(this IMultiplexedStream stream, StreamError errorCode)
        {
            // TODO: XXX: Aborting both read/write triggers the sending of two frames: StopSending frame for AbortRead
            // and Reset frame for AbortWrite
            stream.AbortRead(errorCode);
            stream.AbortWrite(errorCode);
        }

        /// <summary>Reads data from the stream until the given buffer is full.</summary>
        /// <param name="stream">The stream to read data from.</param>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is filled up with read data.</returns>
        internal static async ValueTask ReadUntilFullAsync(
            this IMultiplexedStream stream,
            Memory<byte> buffer,
            CancellationToken cancel)
        {
            // Loop until we received enough data to fully fill the given buffer.
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = await stream.ReadAsync(buffer[offset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException("unexpected end of stream");
                }
                offset += received;
            }
        }
    }
}
