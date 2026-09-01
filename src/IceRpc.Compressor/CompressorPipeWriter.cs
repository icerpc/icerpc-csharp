// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor;

/// <summary>A payload writer decorator that compresses the data written to it. Unlike the pipe writer created by
/// <see cref="PipeWriter.Create(Stream, StreamPipeWriterOptions?)" /> over a compression stream, this decorator
/// implements the completion semantics of payload writers: completing it with an exception completes the decoratee
/// with this exception (and not gracefully), and completing it without an exception never throws — when the writing of
/// the last compressed bytes fails, the decoratee is completed with the exception.</summary>
internal class CompressorPipeWriter : PipeWriter
{
    public override bool CanGetUnflushedBytes => _compressedDataWriter.CanGetUnflushedBytes;

    public override long UnflushedBytes => _compressedDataWriter.UnflushedBytes;

    private readonly PipeWriter _compressedDataWriter;
    private readonly PipeWriter _decoratee;
    private bool _isCompleted;

    public override void Advance(int bytes) => _compressedDataWriter.Advance(bytes);

    public override void CancelPendingFlush() => _compressedDataWriter.CancelPendingFlush();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            if (exception is null && _compressedDataWriter.UnflushedBytes > 0)
            {
                throw new InvalidOperationException(
                    $"Completing a {nameof(CompressorPipeWriter)} without an exception is not allowed when this pipe writer has unflushed bytes.");
            }

            _isCompleted = true;

            if (exception is null)
            {
                try
                {
                    // Writes the compression trailer into the decoratee.
                    _compressedDataWriter.Complete();
                }
                catch (Exception completeException)
                {
                    exception = completeException;
                }

                _decoratee.Complete(exception);
            }
            else
            {
                // Complete the decoratee first: this prevents the disposal of the compression stream below from
                // writing the compression trailer to the transport — such a write could block on flow control with no
                // cancellation. The trailer writes fail immediately on the completed decoratee.
                _decoratee.Complete(exception);
                try
                {
                    // Releases the compression stream.
                    _compressedDataWriter.Complete(exception);
                }
                catch
                {
                    // Expected: see comment above.
                }
            }
        }
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
        _compressedDataWriter.FlushAsync(cancellationToken);

    public override Memory<byte> GetMemory(int sizeHint = 0) => _compressedDataWriter.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _compressedDataWriter.GetSpan(sizeHint);

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken = default) =>
        _compressedDataWriter.WriteAsync(source, cancellationToken);

    internal CompressorPipeWriter(
        PipeWriter decoratee,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel)
    {
        Debug.Assert(compressionFormat is CompressionFormat.Brotli or CompressionFormat.Deflate);

        _decoratee = decoratee;

        // leaveOpen: true so that the completion of the compressed data writer never completes the decoratee.
        Stream decorateeStream = decoratee.AsStream(leaveOpen: true);
        _compressedDataWriter = PipeWriter.Create(
            compressionFormat == CompressionFormat.Brotli ?
                new BrotliStream(decorateeStream, compressionLevel) :
                new DeflateStream(decorateeStream, compressionLevel));
    }
}
