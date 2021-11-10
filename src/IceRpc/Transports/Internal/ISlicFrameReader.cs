// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>A Slic frame reader is used by the Slic transport to read Slic frames.</summary>
    internal interface ISlicFrameReader
    {
        /// <summary>Reads the data from a Slic frame into a buffer.</summary>
        ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Reads a Slic frame header.</summary>
        ValueTask<(FrameType, int, long?)> ReadFrameHeaderAsync(CancellationToken cancel);
    }
}
