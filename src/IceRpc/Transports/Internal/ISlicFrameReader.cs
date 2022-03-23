// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>A Slic frame reader is used by the Slic transport to read Slic frames.</summary>
    internal interface ISlicFrameReader
    {
        /// <summary>The underlying simple network connection reader.</summary>
        // TODO: are we getting rid of this ISlicFrameReader interface?
        SimpleNetworkConnectionReader NetworkConnectionReader { get; }

        /// <summary>Reads a Slic frame header.</summary>
        ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(CancellationToken cancel);
    }
}
