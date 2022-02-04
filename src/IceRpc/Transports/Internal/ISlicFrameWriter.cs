// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Transports.Internal
{
    /// <summary>A Slic frame writer is used by the Slic transport to write Slic frames.</summary>
    internal interface ISlicFrameWriter
    {
        /// <summary>Writes a Slic frame.</summary>
        ValueTask WriteFrameAsync(
            ReadOnlyMemory<byte> slicHeader,
            ReadOnlySequence<byte> protocolHeader,
            ReadOnlySequence<byte> payload,
            CancellationToken cancel);
    }
}
