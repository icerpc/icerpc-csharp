// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Slic;
using System.Buffers;

namespace IceRpc.Transports.Internal.Slic
{
    internal interface ISlicFrameReader : IDisposable
    {
        ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel);
        ValueTask<(FrameType, int)> ReadFrameHeaderAsync(CancellationToken cancel);
        ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel);
    }
}
