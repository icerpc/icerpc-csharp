// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>A Slic frame writer is used by the Slic transport to write Slic frames.</summary>
    internal interface ISlicFrameWriter
    {
        /// <summary>Writes a Slic frame.</summary>
        /// TODO: fix to use IReadOnlyList
        ValueTask WriteFrameAsync(
            SlicMultiplexedStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel);

        /// <summary>Writes a Slic Stream or StreamLast frame.</summary>
        ValueTask WriteStreamFrameAsync(
            SlicMultiplexedStream stream,
            List<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);
    }
}
