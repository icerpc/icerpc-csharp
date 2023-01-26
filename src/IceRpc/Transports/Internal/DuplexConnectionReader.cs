// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>A helper class to efficiently read data from a duplex connection. It provides a PipeReader-like API but is
/// not a PipeReader.</summary>
internal class DuplexConnectionReader : IDisposable
{
    private readonly IDuplexConnection _connection;
    private readonly Pipe _pipe;

    public void Dispose()
    {
        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
    }

    internal DuplexConnectionReader(
        IDuplexConnection connection,
        MemoryPool<byte> pool,
        int minimumSegmentSize)
    {
        _connection = connection;
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));
    }

    internal void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

    internal void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _pipe.Reader.AdvanceTo(consumed, examined);

    /// <summary>Writes <paramref name="byteCount" /> bytes read from this pipe reader or its underlying connection
    /// into <paramref name="bufferWriter" />.</summary>
    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken)
    {
        if (byteCount == 0)
        {
            return default;
        }

        // If there's still data on the pipe reader, copy the data from the pipe reader synchronously.
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);

            ReadOnlySequence<byte> buffer = readResult.Buffer;
            if (buffer.Length > byteCount)
            {
                buffer = buffer.Slice(0, byteCount);
            }

            bufferWriter.Write(buffer);
            _pipe.Reader.AdvanceTo(buffer.End);

            byteCount -= (int)buffer.Length;

            if (byteCount == 0)
            {
                return default;
            }
        }

        return ReadFromConnectionAsync(byteCount);

        // Read the remaining bytes directly from the connection into the buffer writer.
        async ValueTask ReadFromConnectionAsync(int byteCount)
        {
            try
            {
                do
                {
                    Memory<byte> buffer = bufferWriter.GetMemory();
                    if (buffer.Length > byteCount)
                    {
                        buffer = buffer[0..byteCount];
                    }

                    int read = await _connection.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    bufferWriter.Advance(read);
                    byteCount -= read;

                    if (byteCount > 0 && read == 0)
                    {
                        // The peer gracefully shut down the connection but returned less data than expected, it's
                        // considered as an error.
                        throw new InvalidDataException("Received less data than expected.");
                    }
                }
                while (byteCount > 0);
            }
            catch (ObjectDisposedException exception)
            {
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The read operation was aborted by the disposal of the duplex connection.",
                    exception);
            }
        }
    }

    /// <summary>Reads and returns bytes from the underlying transport connection. The returned buffer can be empty if
    /// the peer shutdown its side of the connection.</summary>
    internal ValueTask<ReadOnlySequence<byte>> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsyncCore(minimumSize: 1, canReturnEmptyBuffer: true, cancellationToken);

    /// <summary>Reads and returns bytes from the underlying transport connection. The returned buffer has always
    /// at least minimumSize bytes.</summary>
    internal ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(int minimumSize, CancellationToken cancellationToken = default) =>
        ReadAsyncCore(minimumSize: minimumSize, canReturnEmptyBuffer: false, cancellationToken);

    internal bool TryRead(out ReadOnlySequence<byte> buffer)
    {
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
            buffer = readResult.Buffer;
            return true;
        }
        else
        {
            buffer = default;
            return false;
        }
    }

    /// <summary>Reads and returns bytes from the underlying transport connection. The returned buffer has always at
    /// least minimumSize bytes or if canReturnEmptyBuffer is true, the returned buffer can be empty if the peer
    /// shutdown the connection.</summary>
    private async ValueTask<ReadOnlySequence<byte>> ReadAsyncCore(
        int minimumSize,
        bool canReturnEmptyBuffer,
        CancellationToken cancellationToken = default)
    {
        Debug.Assert(minimumSize > 0);

        // Read buffered data first.
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
            if (readResult.Buffer.Length >= minimumSize)
            {
                return readResult.Buffer;
            }
            _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            minimumSize -= (int)readResult.Buffer.Length;
        }

        try
        {
            do
            {
                // Fill the pipe with data read from the connection.
                Memory<byte> buffer = _pipe.Writer.GetMemory();
                int read = await _connection.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                _pipe.Writer.Advance(read);
                minimumSize -= read;

                // The peer shutdown its side of the connection, return an empty buffer if allowed.
                if (read == 0)
                {
                    if (canReturnEmptyBuffer)
                    {
                        break;
                    }
                    else
                    {
                        // The peer gracefully shut down the connection but returned less data than expected, it's
                        // considered as an error.
                        throw new InvalidDataException("Received less data than expected.");
                    }
                }
            }
            while (minimumSize > 0);
        }
        catch (ObjectDisposedException exception)
        {
            throw new IceRpcException(
                IceRpcError.OperationAborted,
                "The read operation was aborted by the disposal of the duplex connection.",
                exception);
        }

        _ = await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        _pipe.Reader.TryRead(out readResult);
        Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled);

        return readResult.Buffer;
    }
}
