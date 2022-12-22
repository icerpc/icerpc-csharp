// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

/// <summary>The Quic multiplexed stream implements an <see cref="IMultiplexedStream"/>.</summary>
internal class QuicMultiplexedStream : IMultiplexedStream
{
    public ulong Id { get; }

    public PipeReader Input =>
        _inputPipeReader ?? throw new InvalidOperationException("A local unidirectional stream has no Input.");

    public bool IsBidirectional { get; }

    public bool IsRemote { get; }

    public bool IsStarted => true;

    public PipeWriter Output =>
        _outputPipeWriter ?? throw new InvalidOperationException("A remote unidirectional stream has no Output.");

    public Task ReadsClosed => _inputPipeReader?.Closed ?? Task.CompletedTask;

    public Task WritesClosed => _outputPipeWriter?.Closed ?? Task.CompletedTask;

    private readonly QuicPipeReader? _inputPipeReader;
    private readonly QuicPipeWriter? _outputPipeWriter;
    private readonly QuicStream _stream;

    // Disposing of the Quic stream here doesn't work because of some issues with the completion of the ReadsClosed and
    // WritesClosed tasks. Instead the stream is disposed when the output and input are completed. See
    // https://github.com/dotnet/runtime/issues/79818 and https://github.com/dotnet/runtime/issues/79911
    public ValueTask DisposeAsync() => default;

    internal QuicMultiplexedStream(
        QuicStream stream,
        bool isRemote,
        MemoryPool<byte> pool,
        int minSegmentSize)
    {
        Id = (ulong)stream.Id;
        IsRemote = isRemote;
        IsBidirectional = stream.Type == QuicStreamType.Bidirectional;

        int streamCount = stream.CanRead && stream.CanWrite ? 2 : 1;

        _stream = stream;
        _inputPipeReader = stream.CanRead ? new QuicPipeReader(stream, pool, minSegmentSize, DisposeStream) : null;
        _outputPipeWriter = stream.CanWrite ? new QuicPipeWriter(stream, pool, minSegmentSize, DisposeStream) : null;

        void DisposeStream()
        {
            if (--streamCount == 0)
            {
                _ = _stream.DisposeAsync().AsTask();
            }
        }
    }
}
