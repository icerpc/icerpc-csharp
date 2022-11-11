// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal static class PipeWriterExtensions
{
    private static readonly Exception _outputCompleteException = new();

    /// <summary>Completes the output provided by a <see cref="IMultiplexedStream" />.</summary>
    /// <param name="output">The output (a pipe writer).</param>
    /// <param name="success">When <see langword="true" />, the output is completed with a <see langword="null" />
    /// exception. Otherwise, it's completed with an exception. The exception used does not matter since Output behaves
    /// the same when completed with any exception.</param>
    internal static void CompleteOutput(this PipeWriter output, bool success)
    {
        if (success)
        {
            output.Complete(null);
        }
        else
        {
            output.Complete(_outputCompleteException);
        }
    }

    /// <summary>Writes a read only sequence of bytes to this writer.</summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="source">The source sequence.</param>
    /// <param name="endStream">When <see langword="true" />, no more data will be written to the writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The flush result.</returns>
    internal static async ValueTask<FlushResult> WriteAsync(
        this PipeWriter writer,
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (writer is ReadOnlySequencePipeWriter readonlySequenceWriter)
        {
            return await readonlySequenceWriter.WriteAsync(source, endStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            FlushResult flushResult = default;
            if (source.IsEmpty)
            {
                flushResult = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (source.IsSingleSegment)
            {
                flushResult = await writer.WriteAsync(source.First, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // TODO: If readResult.Buffer.Length is small, it might be better to call a single
                // writer.WriteAsync(readResult.Buffer.ToArray()) instead of calling multiple times WriteAsync
                // that will end up in multiple transport calls?
                foreach (ReadOnlyMemory<byte> buffer in source)
                {
                    flushResult = await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
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
