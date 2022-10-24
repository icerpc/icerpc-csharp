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
    public ulong Id { get; }

    public PipeReader Input =>
        _inputPipeReader ??
        throw new InvalidOperationException($"can't get {nameof(Input)} on unidirectional local stream");

    public bool IsBidirectional { get; }

    public bool IsRemote { get; }

    public bool IsStarted => true;

    public PipeWriter Output =>
        _outputPipeWriter ??
        throw new InvalidOperationException($"can't get {nameof(Output)} on unidirectional remote stream");

    public Task InputClosed => _inputPipeReader?.Closed ?? Task.CompletedTask;

    public Task OutputClosed => _outputPipeWriter?.Closed ?? Task.CompletedTask;

    private readonly QuicPipeReader? _inputPipeReader;
    private readonly QuicPipeWriter? _outputPipeWriter;

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
        Id = (ulong)stream.Id;
        IsRemote = isRemote;
        IsBidirectional = stream.Type == QuicStreamType.Bidirectional;

        int streamRefCount = 0;

        if (stream.CanRead)
        {
            streamRefCount++;

            _inputPipeReader = new QuicPipeReader(
                stream,
                errorCodeConverter,
                pool,
                minSegmentSize,
                OnCompleted);
        }

        if (stream.CanWrite)
        {
            streamRefCount++;

            _outputPipeWriter = new QuicPipeWriter(
                stream,
                errorCodeConverter,
                pool,
                minSegmentSize,
                OnCompleted);
        }

        void OnCompleted()
        {
            if (Interlocked.Decrement(ref streamRefCount) == 0)
            {
                // The callback is called from the pipe reader/writer non-async Complete method so we just initiate the
                // stream disposal and it will eventually complete in the background.
                _ = stream.DisposeAsync().AsTask();
            }
        }
    }
}
