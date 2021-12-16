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

            if (complete)
            {
                CancellationToken savedToken = CompleteCancellationToken;
                CompleteCancellationToken = cancel;
                try
                {
                    FlushResult result;
                    try
                    {
                        result = await this.WriteAsync(source, cancel).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await CompleteAsync(ex).ConfigureAwait(false);
                        throw;
                    }
                    await CompleteAsync().ConfigureAwait(false);
                    return result;
                }
                finally
                {
                    CompleteCancellationToken = savedToken;
                }
            }
            else
            {
                return await this.WriteAsync(source, cancel).ConfigureAwait(false);
            }
        }
    }
}
