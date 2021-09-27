// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports.Slic;

namespace IceRpc.Transports.Internal.Slic
{
    internal interface ISlicFrameWriter : IDisposable
    {
        ValueTask WriteFrameAsync(FrameType type, Action<IceEncoder> encode, CancellationToken cancel);

        ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            FrameType type,
            Action<IceEncoder> encode,
            CancellationToken cancel);

        ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);
    }
}
