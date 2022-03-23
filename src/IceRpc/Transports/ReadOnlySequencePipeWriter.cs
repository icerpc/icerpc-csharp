// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>The <see cref="ReadOnlySequencePipeWriter"/> abstract class can be extended by pipe writers to provide
    /// a <see cref="PipeWriter.WriteAsync"/> method with a <see cref="ReadOnlySequence{T}"/> source. It also provides a
    /// boolean to notify the pipe writer implementation that no more data will be written. This class is useful for
    /// implementing multiplexed stream pipe writers and to optimize the writing of a <see cref="ReadOnlySequence{T}"/>
    /// for transports that support a gather write API.</summary>
    public abstract class ReadOnlySequencePipeWriter : PipeWriter
    {
        /// <summary>Writes a readonly sequence.</summary>
        /// <param name="source">The source sequence.</param>
        /// <param name="endStream">If <c>true</c>, no more data will be written to this pipe.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The flush result.</returns>
        public abstract ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancel);
    }
}
