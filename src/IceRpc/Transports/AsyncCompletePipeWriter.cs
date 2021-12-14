// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>A PipeWriter that must be completed asynchronously using <see cref="PipeWriter.CompleteAsync"/>.
    /// </summary>
    public abstract class AsyncCompletePipeWriter : PipeWriter
    {
        /// <summary>The cancellation token used by <see cref="PipeWriter.CompleteAsync"/> for any async call it makes.
        /// </summary>
        public CancellationToken CompleteCancellationToken { get; set; }

        /// <summary>Copies source to this writer and optionally completes this writer.</summary>
        /// <param name="source">The source pipe reader.</param>
        /// <param name="completeWhenDone">When true, this method completes the writer after a successful copy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>This method does not use or update <see cref="CompleteCancellationToken"/>.</remarks>
        public abstract Task CopyFromAsync(
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel);
    }
}
