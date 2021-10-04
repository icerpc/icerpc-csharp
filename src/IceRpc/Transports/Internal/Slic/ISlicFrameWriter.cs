// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>A Slic frame writer is used by the Slic transport to write Slic frames.</summary>
    internal interface ISlicFrameWriter : IDisposable
    {
        ValueTask WriteFrameAsync(
            SlicStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel);

        ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);
    }
}
