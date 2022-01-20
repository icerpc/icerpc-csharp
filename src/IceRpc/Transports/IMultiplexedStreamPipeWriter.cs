// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>This inferface can be implemented by the multiplexed stream output pipe writer to provide a <see
    /// cref="PipeWriter.WriteAsync"/> method that supports a <see cref="ReadOnlySequence{T}"/> source and ending the
    /// completing the writer. This interface is used by the IceRPC core to optimize the copy of a <see
    /// cref="PipeReader"/> to a pipe writer that supports this interface.</summary>
    internal interface IMultiplexedStreamPipeWriter
    {
        /// <summary>Writes a readonly sequence and eventually completes the pipe writer once the write
        /// completes.</summary>
        /// <param name="source">The source sequence.</param>
        /// <param name="completeWhenDone">If <c>true</c>, this method completes the writer after a successful
        /// write.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The flush result.</returns>
        ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel);
    }
}
