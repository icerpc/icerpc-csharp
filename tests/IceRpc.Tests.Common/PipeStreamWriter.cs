// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A multiplexed stream writer that writes into a <see cref="PipeWriter" />, typically the writer of an
/// in-memory <see cref="Pipe" /> whose reader the test checks.</summary>
public sealed class PipeStreamWriter : IMultiplexedStreamWriter
{
    /// <summary>Gets a value indicating whether this writer is completed, by <see cref="CompleteAsync" /> or
    /// <see cref="Abort" />.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Gets a value indicating whether this writer completed successfully: <see cref="CompleteAsync" /> was
    /// called and did not throw.</summary>
    public bool IsCompletedSuccessfully { get; private set; }

    /// <summary>Gets a value indicating whether a write with endStream was performed.</summary>
    public bool IsEnded { get; private set; }

    /// <inheritdoc/>
    public long UnflushedBytes => _pipeWriter.UnflushedBytes;

    private readonly PipeWriter _pipeWriter;

    /// <inheritdoc/>
    public void Advance(int count) => _pipeWriter.Advance(count);

    /// <inheritdoc/>
    public void Abort()
    {
        if (!IsCompleted)
        {
            IsCompleted = true;
            _pipeWriter.Complete(new IceRpcException(IceRpcError.OperationAborted));
        }
    }

    /// <inheritdoc/>
    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (!IsCompleted)
        {
            try
            {
                if (!IsEnded)
                {
                    await _pipeWriter.FlushAsync(cancellationToken);
                }
            }
            catch
            {
                Abort();
                throw;
            }
            IsCompleted = true;
            IsCompletedSuccessfully = true;
            _pipeWriter.Complete();
        }
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0) => _pipeWriter.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => _pipeWriter.GetSpan(sizeHint);

    /// <inheritdoc/>
    public async ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (IsCompleted || IsEnded)
        {
            throw new InvalidOperationException("Writing is not allowed once the writer is completed or ended.");
        }
        IsEnded = endStream;

        foreach (ReadOnlyMemory<byte> segment in source)
        {
            _pipeWriter.Write(segment.Span);
        }
        FlushResult flushResult = await _pipeWriter.FlushAsync(cancellationToken);
        return flushResult.IsCompleted ? WriteResult.WritesClosed : WriteResult.Written;
    }

    /// <summary>Constructs a pipe stream writer.</summary>
    /// <param name="pipeWriter">The pipe writer that receives the bytes.</param>
    public PipeStreamWriter(PipeWriter pipeWriter) => _pipeWriter = pipeWriter;
}
