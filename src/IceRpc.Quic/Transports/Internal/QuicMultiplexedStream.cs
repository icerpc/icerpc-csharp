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

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    internal QuicMultiplexedStream(
        QuicStream stream,
        bool isRemote,
        MemoryPool<byte> pool,
        int minSegmentSize)
    {
        Id = (ulong)stream.Id;
        IsRemote = isRemote;
        IsBidirectional = stream.Type == QuicStreamType.Bidirectional;

        _stream = stream;
        _inputPipeReader = stream.CanRead ? new QuicPipeReader(stream, pool, minSegmentSize) : null;
        _outputPipeWriter = stream.CanWrite ? new QuicPipeWriter(stream, pool, minSegmentSize) : null;
    }
}
