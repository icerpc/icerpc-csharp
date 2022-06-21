// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.Transports.Internal;

/// <summary>A Slic frame writer is used by the Slic transport to write Slic frames.</summary>
internal interface ISlicFrameWriter
{
    /// <summary>Writes a Slic frame.</summary>
    /// <param name="frameType">The frame type.</param>
    /// <param name="streamId">The streamID or null if the frame doesn't include a stream ID.</param>
    /// <param name="encodeAction">The action used to encode the frame body or null if the frame doesn't
    /// contain a body.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask WriteFrameAsync(
        FrameType frameType,
        long? streamId,
        EncodeAction? encodeAction,
        CancellationToken cancel);

    /// <summary>Writes a stream Slic frame.</summary>
    /// <param name="streamId">The stream ID.</param>
    /// <param name="source1">The frame data.</param>
    /// <param name="source2">The frame additional data.</param>
    /// <param name="endStream">Whether to end the stream or not.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask WriteStreamFrameAsync(
        long streamId,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancel);
}
