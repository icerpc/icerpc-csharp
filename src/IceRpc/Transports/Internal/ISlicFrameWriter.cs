// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.Transports.Internal
{
    /// <summary>A Slic frame writer is used by the Slic transport to write Slic frames.</summary>
    internal interface ISlicFrameWriter
    {
        /// <summary>Writes a Slic frame.</summary>
        ValueTask WriteFrameAsync(
            FrameType frameType,
            long? streamId,
            EncodeAction? encode,
            CancellationToken cancel);

        /// <summary>Writes a stream Slic frame.</summary>
        ValueTask WriteStreamFrameAsync(
            long streamId,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel);
    }
}
