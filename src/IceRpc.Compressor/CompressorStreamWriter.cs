// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor;

/// <summary>A payload writer decorator that compresses the bytes written to it. The compression stream writes into an
/// in-memory pipe that this decorator drains into the decoratee with the caller's cancellation token, so that no
/// network write, including the write of the compression trailer, escapes cancellation.</summary>
internal class CompressorStreamWriter : IMultiplexedStreamWriter
{
    public long UnflushedBytes => _uncompressedPipe.Writer.UnflushedBytes;

    // The maximum number of bytes given to the compression stream in one write. The compressed bytes are drained into
    // the decoratee after each chunk, so a large write is sent progressively and observes cancellation between chunks.
    // How many bytes a chunk produces is up to the compression stream, which can buffer its input and emit it later.
    private const int MaxChunkSize = 32 * 1024;

    // Receives the compressed bytes written by _compressionStream; DrainAsync empties it into _decoratee.
    private readonly Pipe _compressedPipe;

    // The Brotli or Deflate stream that compresses the bytes into _compressedPipe.
    private readonly Stream _compressionStream;

    private readonly IMultiplexedStreamWriter _decoratee;
    private bool _isCompleted;

    // Set once the compression is finished, by a WriteAsync call with endStream or by CompleteAsync.
    private bool _isEnded;

    // Buffers the bytes written with the IBufferWriter<byte> methods until the next WriteAsync or CompleteAsync.
    private readonly Pipe _uncompressedPipe;

    public void Advance(int count)
    {
        ThrowIfWritingNotAllowed();
        _uncompressedPipe.Writer.Advance(count);
    }

    public void Abort()
    {
        if (!_isCompleted)
        {
            _isCompleted = true;

            // Aborting the decoratee first makes sure the disposal of the compression stream below can't write
            // anything to the network: its trailer lands in _compressedPipe and is discarded.
            _decoratee.Abort();
            _compressionStream.Dispose();

            _uncompressedPipe.Writer.Complete();
            _uncompressedPipe.Reader.Complete();
            _compressedPipe.Writer.Complete();
            _compressedPipe.Reader.Complete();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (!_isCompleted)
        {
            try
            {
                if (!_isEnded)
                {
                    // Finish the compression: send the buffered bytes and the compression trailer, and end the
                    // stream. The result is ignored because the peer having closed its reads is not a failure here.
                    _ = await WriteAsync(ReadOnlySequence<byte>.Empty, endStream: true, cancellationToken)
                        .ConfigureAwait(false);
                }
                await _decoratee.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Abort();
                throw;
            }

            _isCompleted = true;
            _compressionStream.Dispose();

            _uncompressedPipe.Writer.Complete();
            _uncompressedPipe.Reader.Complete();
            _compressedPipe.Writer.Complete();
            _compressedPipe.Reader.Complete();
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0) => _uncompressedPipe.Writer.GetMemory(sizeHint);

    public Span<byte> GetSpan(int sizeHint = 0) => _uncompressedPipe.Writer.GetSpan(sizeHint);

    public async ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        ThrowIfWritingNotAllowed();
        _isEnded = endStream;

        // Compress the bytes written with GetSpan, GetMemory, and Advance first, since they come before source.
        if (_uncompressedPipe.Writer.UnflushedBytes > 0)
        {
            await _uncompressedPipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            bool tryReadOk = _uncompressedPipe.Reader.TryRead(out ReadResult readResult);
            Debug.Assert(tryReadOk);
            WriteResult result;
            try
            {
                result = await CompressAsync(readResult.Buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _uncompressedPipe.Reader.AdvanceTo(readResult.Buffer.End);
            }

            // CompressAsync drains the compressed bytes into the decoratee as it goes. Once the peer has closed its
            // reads, there is nothing left to compress or send.
            if (result == WriteResult.WritesClosed)
            {
                return result;
            }
        }

        // Then compress source.
        if (await CompressAsync(source, cancellationToken).ConfigureAwait(false) == WriteResult.WritesClosed)
        {
            return WriteResult.WritesClosed;
        }

        if (endStream)
        {
            // Finishes the compression: this writes the compression trailer into _compressedPipe.
            await _compressionStream.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            // Makes the compression stream emit the compressed bytes it holds, so that the peer receives what the
            // application wrote.
            await _compressionStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // The last drain always writes, with endStream when requested and to flush the decoratee otherwise.
        return await DrainAsync(endStream, force: true, cancellationToken).ConfigureAwait(false);
    }

    internal CompressorStreamWriter(
        IMultiplexedStreamWriter decoratee,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel)
    {
        Debug.Assert(compressionFormat is CompressionFormat.Brotli or CompressionFormat.Deflate);

        _decoratee = decoratee;

        // pauseWriterThreshold: 0 so that the writes to these in-memory pipes never block.
        _uncompressedPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));
        _compressedPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));

        // leaveOpen: true because this class completes _compressedPipe.Writer itself.
        Stream compressedStream = _compressedPipe.Writer.AsStream(leaveOpen: true);

        _compressionStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(compressedStream, compressionLevel) :
            new DeflateStream(compressedStream, compressionLevel);
    }

    /// <summary>Compresses <paramref name="source" /> chunk by chunk, draining the compressed bytes into the decoratee
    /// after each chunk.</summary>
    /// <returns><see cref="WriteResult.WritesClosed" /> as soon as a drain reports that the peer closed its reads,
    /// without compressing the rest of <paramref name="source" />; otherwise <see cref="WriteResult.Written" />.
    /// </returns>
    private async ValueTask<WriteResult> CompressAsync(
        ReadOnlySequence<byte> source,
        CancellationToken cancellationToken)
    {
        foreach (ReadOnlyMemory<byte> segment in source)
        {
            for (int offset = 0; offset < segment.Length; offset += MaxChunkSize)
            {
                // This write goes to _compressedPipe, in memory: it can't block, so it doesn't need the token.
                await _compressionStream.WriteAsync(
                    segment.Slice(offset, Math.Min(MaxChunkSize, segment.Length - offset)),
                    CancellationToken.None).ConfigureAwait(false);

                WriteResult result = await DrainAsync(endStream: false, force: false, cancellationToken)
                    .ConfigureAwait(false);
                if (result != WriteResult.Written)
                {
                    return result;
                }
            }
        }
        return WriteResult.Written;
    }

    /// <summary>Writes the compressed bytes held by <see cref="_compressedPipe" /> into the decoratee.</summary>
    /// <param name="endStream">The endStream value for the write.</param>
    /// <param name="force">When <see langword="true" />, writes even when there is no compressed byte, to flush the
    /// decoratee or end the stream.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    private async ValueTask<WriteResult> DrainAsync(bool endStream, bool force, CancellationToken cancellationToken)
    {
        if (_compressedPipe.Reader.TryRead(out ReadResult readResult))
        {
            try
            {
                return await _decoratee.WriteAsync(readResult.Buffer, endStream, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _compressedPipe.Reader.AdvanceTo(readResult.Buffer.End);
            }
        }
        else if (force)
        {
            return await _decoratee.WriteAsync(ReadOnlySequence<byte>.Empty, endStream, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            return WriteResult.Written;
        }
    }

    private void ThrowIfWritingNotAllowed()
    {
        if (_isCompleted || _isEnded)
        {
            throw new InvalidOperationException(
                "Writing is not allowed once the writer is completed or the stream is ended.");
        }
    }
}
