// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>This inferface can be implemented by the multiplexed stream output pipe writer to provide a <see
    /// cref="PipeWriter.WriteAsync"/> method with a <see cref="ReadOnlySequence{T}"/> source. It's also possible to
    /// complete the pipe writer. The IceRPC core to optimize the copy of a <see cref="PipeReader"/> to a pipe writer
    /// that supports this interface.</summary>
    /// TODO: Should this be part of the IceRPC Core API instead of the transport API? Better name?
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
