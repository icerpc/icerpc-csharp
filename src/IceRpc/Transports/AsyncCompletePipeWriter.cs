// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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
        /// <returns>The flush result from the last WriteAsync or a FlushAsync if there was no WriteAsync.</returns>
        public virtual Task<FlushResult> CopyFromAsync(
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel) => CopyFromAsync(source, this, completeWhenDone, cancel);

        /// <summary>Copies source to a sink pipe writer, where sink is a decorator for this pipe writer. Also
        /// optionally completes this pipe writer.</summary>
        /// <param name="source">The source pipe reader.</param>
        /// <param name="sink">The sink pipe writer.</param>
        /// <param name="completeWhenDone">When true, this method completes the writer after a successful copy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result from the last WriteAsync or the FlushAsync if there was no WriteAsync.</returns>
        /// <remarks>This method also provides the default implementation for the public CopyFromAsync.</remarks>
        internal async Task<FlushResult> CopyFromAsync(
            PipeReader source,
            PipeWriter sink,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            CancellationToken saved = CompleteCancellationToken;
            CompleteCancellationToken = cancel;

            try
            {
                try
                {
                    await source.CopyToAsync(sink, cancel).ConfigureAwait(false);
                }
                catch (TaskCanceledException) // CopyToAsync returns a canceled task if cancel is canceled
                {
                    throw new OperationCanceledException();
                }

                // We need to call FlushAsync in case PayloadSource was empty and CopyToAsync didn't do anything. This
                // also gives us the flush result.
                FlushResult flushResult = await sink.FlushAsync(cancel).ConfigureAwait(false);

                if (!flushResult.IsCompleted && !flushResult.IsCanceled && completeWhenDone)
                {
                    await sink.CompleteAsync().ConfigureAwait(false);

                    if (sink != this)
                    {
                        // Need to call CompleteAsync on 'this' in case sink.CompleteAsync did not reach this
                        // CompleteAsync, for example because sink uses a Stream that ends up calling Complete instead
                        // of CompleteAsync. If it did reach it the first time around, it's no-op.
                        await CompleteAsync().ConfigureAwait(false);
                    }
                }

                return flushResult;
            }
            finally
            {
                CompleteCancellationToken = saved;
            }
        }
    }
}
