// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>The <see cref="ReadOnlySequencePipeWriter"/> abstract class can be extended by pipe writers to provide
    /// a <see cref="PipeWriter.WriteAsync"/> method with a <see cref="ReadOnlySequence{T}"/> source. It also provides a
    /// boolean to complete the pipe writer once the write is done. The IceRPC core optimizes the copy of a <see
    /// cref="PipeReader"/> to a <see cref="ReadOnlySequencePipeWriter"/>.</summary>
    public abstract class ReadOnlySequencePipeWriter : PipeWriter
    {
        /// <summary>Writes a readonly sequence and eventually completes the pipe writer once the write
        /// completes.</summary>
        /// <param name="source">The source sequence.</param>
        /// <param name="completeWhenDone">If <c>true</c>, this method completes the pipe writer after a successful
        /// write.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The flush result.</returns>
        public abstract ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel);
    }
}
