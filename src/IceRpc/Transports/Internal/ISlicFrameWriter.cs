// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>A Slic frame writer is used by the Slic transport to write Slic frames.</summary>
    internal interface ISlicFrameWriter
    {
        /// <summary>Writes a Slic frame.</summary>
        ValueTask WriteFrameAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}
