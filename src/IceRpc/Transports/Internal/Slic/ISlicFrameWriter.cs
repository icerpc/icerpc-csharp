// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>A Slic frame writer is used by the Slic transport to write Slic frames. The writer is
    /// disposable to allow implementations to rely on disposable resources.</summary>
    internal interface ISlicFrameWriter : IDisposable
    {
        /// <summary>Writes a Slic frame.</summary>
        ValueTask WriteFrameAsync(
            SlicStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel);

        /// <summary>Writes a Slic Stream or StreamLast frame.</summary>
        ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);
    }
}
