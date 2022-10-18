// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

/// <summary>The Quic multiplexed stream implements an <see cref="IMultiplexedStream"/>.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicMultiplexedStream : IMultiplexedStream
{
    public ulong Id => (ulong)_stream.Id;

    public PipeReader Input =>
        _inputPipeReader ??
        throw new InvalidOperationException($"can't get {nameof(Input)} on unidirectional local stream");

    public bool IsBidirectional => _stream.Type == QuicStreamType.Bidirectional;

    public bool IsRemote { get; }

    public bool IsStarted => true;

    public PipeWriter Output =>
        _outputPipeWriter ??
        throw new InvalidOperationException($"can't get {nameof(Output)} on unidirectional remote stream");

    public Task ReadsClosed => _inputPipeReader?.ReadsClosed ?? Task.CompletedTask;

    public Task WritesClosed => _outputPipeWriter?.WritesClosed ?? Task.CompletedTask;

    private int _streamRefCount;
    private readonly QuicPipeReader? _inputPipeReader;
    private readonly QuicPipeWriter? _outputPipeWriter;
    private readonly QuicStream _stream;

    public void Abort(Exception completeException)
    {
        _inputPipeReader?.Abort(completeException);
        _outputPipeWriter?.Abort(completeException);
    }

    internal QuicMultiplexedStream(
        QuicStream stream,
        bool isRemote,
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        MemoryPool<byte> pool,
        int minSegmentSize)
    {
        IsRemote = isRemote;

        _stream = stream;
        _streamRefCount = 0;

        if (_stream.CanRead)
        {
            _streamRefCount++;

            _inputPipeReader = new QuicPipeReader(
                _stream,
                errorCodeConverter,
                pool,
                minSegmentSize,
                OnCompleted);
        }

        if (_stream.CanWrite)
        {
            _streamRefCount++;

            _outputPipeWriter = new QuicPipeWriter(
                _stream,
                errorCodeConverter,
                pool,
                minSegmentSize,
                OnCompleted);
        }

        void OnCompleted()
        {
            if (Interlocked.Decrement(ref _streamRefCount) == 0)
            {
                // The callback is called from the pipe reader/writer non-async Complete method so we just initiate the
                // stream disposal and it will eventually complete in the background.
                _ = _stream.DisposeAsync().AsTask();
            }
        }
    }
}
