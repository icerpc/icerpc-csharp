// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Slic;
using System.Buffers;

namespace IceRpc.Transports.Internal.Slic
{
    internal interface ISlicFrameReader : IDisposable
    {
        ValueTask<(FrameType, int, IMemoryOwner<byte>)> ReadFrameAsync(CancellationToken cancel);
        ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel);
        ValueTask ReadStreamFrameDataAsync(Memory<byte> buffer, CancellationToken cancel);
        ValueTask<IMemoryOwner<byte>> ReadStreamFrameDataAsync(int length, CancellationToken cancel);
    }
}
