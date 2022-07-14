// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A stateless pipe writer used for the payload writer of an outgoing request or response. The writer just
/// writes to the transport connection writer.</summary>
internal sealed class IcePayloadPipeWriter : ReadOnlySequencePipeWriter
{
    private readonly SingleStreamTransportConnectionWriter _transportConnectionWriter;

    public override void Advance(int bytes) => _transportConnectionWriter.Advance(bytes);

    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        // No-op. We don't want to dispose the transport connection writer.
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancel = default)
    {
        try
        {
            await _transportConnectionWriter.FlushAsync(cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The single stream transport connection can only be disposed if this connection is aborted.
            throw new ConnectionAbortedException();
        }
        return default;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _transportConnectionWriter.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _transportConnectionWriter.GetSpan(sizeHint);

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel)
    {
        try
        {
            await _transportConnectionWriter.WriteAsync(
                new ReadOnlySequence<byte>(source), cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The single stream transport connection can only be disposed if this connection is aborted.
            throw new ConnectionAbortedException();
        }
        return default;
    }

    /// <summary>Writes the source to the single stream transport connection. <paramref name="endStream"/> is ignored
    /// because the single stream transport connection has no use for it.</summary>
    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancel)
    {
        try
        {
            await _transportConnectionWriter.WriteAsync(source, cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The single stream transport connection can only be disposed if this connection is aborted.
            throw new ConnectionAbortedException();
        }
        return default;
    }

    internal IcePayloadPipeWriter(SingleStreamTransportConnectionWriter transportConnectionWriter) =>
        _transportConnectionWriter = transportConnectionWriter;
}
