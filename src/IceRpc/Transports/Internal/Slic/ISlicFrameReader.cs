// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Slic;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>A Slic frame reader is used by the Slic transport to read Slic frames.</summary>
    internal interface ISlicFrameReader : IDisposable
    {
        ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel);
        ValueTask<(FrameType, int)> ReadFrameHeaderAsync(CancellationToken cancel);
        ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel);
    }
}
