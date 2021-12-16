// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
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

        /// <summary>Writes a read only sequence of bytes to this writer and optionally completes this writer.</summary>
        /// <param name="source">The source sequence.</param>
        /// <param name="complete">When true, this method completes the writer once the write operation completes
        /// successfully or with an exception.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        public virtual async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool complete,
            CancellationToken cancel)
        {
            // This is the non-optimized implementation. Derived classes are expected to override this method with an
            // optimized implementation.

            try
            {
                FlushResult result = await this.WriteAsync(source, cancel).ConfigureAwait(false);

                if (complete)
                {
                    await CancellableCompleteAsync(exception: null, cancel).ConfigureAwait(false);
                }

                return result;
            }
            catch (Exception ex)
            {
                if (complete)
                {
                    await CancellableCompleteAsync(ex, cancel).ConfigureAwait(false);
                }
                throw;
            }

            async ValueTask CancellableCompleteAsync(Exception? exception, CancellationToken cancel)
            {
                CancellationToken savedToken = CompleteCancellationToken;
                CompleteCancellationToken = cancel;
                try
                {
                    await CompleteAsync(exception).ConfigureAwait(false);
                }
                finally
                {
                    CompleteCancellationToken = savedToken;
                }
            }
        }
    }
}
