// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal static class PipeWriterExtensions
    {
        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="writer">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="endStream">When true, no more data will be written to the sink.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async ValueTask<FlushResult> WriteAsync(
            this PipeWriter writer,
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancel)
        {
            if (writer is ReadOnlySequencePipeWriter readonlySequenceWriter)
            {
                return await readonlySequenceWriter.WriteAsync(source, endStream, cancel).ConfigureAwait(false);
            }
            else
            {
                FlushResult flushResult = default;
                if (source.IsEmpty)
                {
                    flushResult = await writer.FlushAsync(cancel).ConfigureAwait(false);
                }
                else if (source.IsSingleSegment)
                {
                    flushResult = await writer.WriteAsync(source.First, cancel).ConfigureAwait(false);
                }
                else
                {
                    // TODO: If readResult.Buffer.Length is small, it might be better to call a single
                    // sink.WriteAsync(readResult.Buffer.ToArray()) instead of calling multiple times WriteAsync
                    // that will end up in multiple network calls?
                    foreach (ReadOnlyMemory<byte> buffer in source)
                    {
                        flushResult = await writer.WriteAsync(buffer, cancel).ConfigureAwait(false);
                        if (flushResult.IsCompleted || flushResult.IsCanceled)
                        {
                            break;
                        }
                    }
                }
                return flushResult;
            }
        }

        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="writer">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async ValueTask<FlushResult> WriteAsync(
            this PipeWriter writer,
            ReadOnlySequence<byte> source,
            CancellationToken cancel)
        {
            if (writer is IcePayloadPipeWriter icePayloadPipeWriter)
            {
                await icePayloadPipeWriter.WriteAsync(source, cancel).ConfigureAwait(false);
                return default;
            }
            else
            {
                FlushResult flushResult = default;
                if (source.IsEmpty)
                {
                    flushResult = await writer.FlushAsync(cancel).ConfigureAwait(false);
                }
                else if (source.IsSingleSegment)
                {
                    flushResult = await writer.WriteAsync(source.First, cancel).ConfigureAwait(false);
                }
                else
                {
                    // TODO: If readResult.Buffer.Length is small, it might be better to call a single
                    // sink.WriteAsync(readResult.Buffer.ToArray()) instead of calling multiple times WriteAsync
                    // that will end up in multiple network calls?
                    foreach (ReadOnlyMemory<byte> buffer in source)
                    {
                        flushResult = await writer.WriteAsync(buffer, cancel).ConfigureAwait(false);
                        if (flushResult.IsCompleted || flushResult.IsCanceled)
                        {
                            break;
                        }
                    }
                }
                return flushResult;
            }
        }
    }
}
