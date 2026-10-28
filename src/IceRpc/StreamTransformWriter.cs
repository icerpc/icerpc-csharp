// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Decorates an <see cref="IMultiplexedStreamWriter" /> with a <see cref="Stream" /> transform, such as a
/// <see cref="System.IO.Compression.BrotliStream" /> or a <c>CryptoStream</c>. The bytes written to this writer flow
/// through the transform stream; the bytes the transform stream writes to its inner stream are sent to the decoratee.
/// </summary>
/// <remarks>The transform stream must write synchronously, and only to the inner stream it is given. Each
/// <see cref="WriteAsync" /> call flushes the transform stream and sends what this flush emits; a transform stream can
/// keep part of its input until the stream ends. Ending the stream disposes the transform stream and sends its
/// trailing bytes.</remarks>
public sealed class StreamTransformWriter : IMultiplexedStreamWriter
{
    /// <inheritdoc/>
    public long UnflushedBytes => _inputPipe.Writer.UnflushedBytes;

    private readonly IMultiplexedStreamWriter _decoratee;

    // Buffers the bytes written with the IBufferWriter<byte> methods until the next WriteAsync or CompleteAsync.
    private readonly Pipe _inputPipe;

    private bool _isCompleted;

    // Set once the transform is finished, by a WriteAsync call with endStream or by CompleteAsync.
    private bool _isEnded;

    private readonly int _maxChunkSize;

    // Receives the bytes written by _transformStream; SendAsync sends them to _decoratee.
    private readonly Pipe _outputPipe;

    private readonly Stream _transformStream;

    /// <summary>Constructs a stream transform writer.</summary>
    /// <param name="decoratee">The writer that receives the transformed bytes.</param>
    /// <param name="transform">Creates the transform stream over a write-only, in-memory inner stream.</param>
    /// <param name="maxChunkSize">The maximum number of bytes given to the transform stream in one write. The bytes it
    /// produces are sent to the decoratee after each chunk, so this value bounds how many transformed bytes are held
    /// in memory, and a large write observes cancellation between chunks. How many bytes a chunk produces is up to the
    /// transform stream, which can buffer its input and emit it later.</param>
    public StreamTransformWriter(
        IMultiplexedStreamWriter decoratee,
        Func<Stream, Stream> transform,
        int maxChunkSize = 32 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkSize);

        _decoratee = decoratee;
        _maxChunkSize = maxChunkSize;

        // pauseWriterThreshold: 0 so that the writes to these in-memory pipes never block.
        _inputPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));
        _outputPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));

        // leaveOpen: true because this class completes _outputPipe.Writer itself.
        _transformStream = transform(_outputPipe.Writer.AsStream(leaveOpen: true));
    }

    /// <inheritdoc/>
    public void Advance(int count)
    {
        ThrowIfWritingNotAllowed();
        _inputPipe.Writer.Advance(count);
    }

    /// <inheritdoc/>
    public void Abort()
    {
        if (!_isCompleted)
        {
            _isCompleted = true;

            // The disposal of the transform stream below can't write anything to the network: its trailing bytes land
            // in _outputPipe and are discarded with it.
            _decoratee.Abort();
            _transformStream.Dispose();

            _inputPipe.Writer.Complete();
            _inputPipe.Reader.Complete();
            _outputPipe.Writer.Complete();
            _outputPipe.Reader.Complete();
        }
    }

    /// <inheritdoc/>
    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (!_isCompleted)
        {
            try
            {
                if (!_isEnded)
                {
                    // Finish the transform: send the buffered bytes and the trailing bytes, and end the stream. The
                    // result is ignored because the peer having closed its reads is not a failure here.
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

            // A no-op unless the endStream write returned early with WritesClosed: the trailing bytes it then writes
            // land in _outputPipe and are discarded with it.
            _transformStream.Dispose();

            _inputPipe.Writer.Complete();
            _inputPipe.Reader.Complete();
            _outputPipe.Writer.Complete();
            _outputPipe.Reader.Complete();
        }
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0) => _inputPipe.Writer.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => _inputPipe.Writer.GetSpan(sizeHint);

    /// <inheritdoc/>
    public async ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        ThrowIfWritingNotAllowed();
        _isEnded = endStream;

        // Transform the bytes written with GetSpan, GetMemory, and Advance first, since they come before source.
        if (_inputPipe.Writer.UnflushedBytes > 0)
        {
            await _inputPipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            bool tryReadOk = _inputPipe.Reader.TryRead(out ReadResult readResult);
            Debug.Assert(tryReadOk);
            WriteResult result;
            try
            {
                result = await TransformAsync(readResult.Buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _inputPipe.Reader.AdvanceTo(readResult.Buffer.End);
            }

            // TransformAsync sends the transformed bytes to the decoratee as it goes. Once the peer has closed its
            // reads, there is nothing left to transform or send.
            if (result == WriteResult.WritesClosed)
            {
                return result;
            }
        }

        // Then transform source.
        if (await TransformAsync(source, cancellationToken).ConfigureAwait(false) == WriteResult.WritesClosed)
        {
            return WriteResult.WritesClosed;
        }

        if (endStream)
        {
            // Finishes the transform: this writes the trailing bytes into _outputPipe.
            _transformStream.Dispose();
        }
        else
        {
            // Makes the transform stream emit the bytes it holds.
            _transformStream.Flush();
        }

        // The last send always writes, with endStream when requested and to flush the decoratee otherwise.
        return await SendAsync(endStream, force: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends the bytes held by <see cref="_outputPipe" /> to the decoratee.</summary>
    /// <param name="endStream">The endStream value for the write.</param>
    /// <param name="force">When <see langword="true" />, writes even when there is no transformed byte, to flush the
    /// decoratee or end the stream.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    private async ValueTask<WriteResult> SendAsync(bool endStream, bool force, CancellationToken cancellationToken)
    {
        if (_outputPipe.Reader.TryRead(out ReadResult readResult))
        {
            try
            {
                return await _decoratee.WriteAsync(readResult.Buffer, endStream, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _outputPipe.Reader.AdvanceTo(readResult.Buffer.End);
            }
        }
        else if (force)
        {
            return await _decoratee.WriteAsync(ReadOnlySequence<byte>.Empty, endStream, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // The transform buffered its input without producing anything to send, so the decoratee can't observe
            // the token for this chunk.
            cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>Transforms <paramref name="source" /> chunk by chunk, sending the transformed bytes to the decoratee
    /// after each chunk.</summary>
    /// <returns><see cref="WriteResult.WritesClosed" /> as soon as a send reports that the peer closed its reads,
    /// without transforming the rest of <paramref name="source" />; otherwise <see cref="WriteResult.Written" />.
    /// </returns>
    private async ValueTask<WriteResult> TransformAsync(
        ReadOnlySequence<byte> source,
        CancellationToken cancellationToken)
    {
        foreach (ReadOnlyMemory<byte> memory in source)
        {
            ReadOnlyMemory<byte> segment = memory;
            while (!segment.IsEmpty)
            {
                ReadOnlyMemory<byte> chunk = segment[..Math.Min(_maxChunkSize, segment.Length)];
                _transformStream.Write(chunk.Span);
                segment = segment[chunk.Length..];

                WriteResult result = await SendAsync(endStream: false, force: false, cancellationToken)
                    .ConfigureAwait(false);
                if (result != WriteResult.Written)
                {
                    return result;
                }
            }
        }
        return WriteResult.Written;
    }
}
