// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal.Slic
{
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
