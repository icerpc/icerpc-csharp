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
    private readonly DuplexConnectionWriter _transportConnectionWriter;

    public override void Advance(int bytes) => _transportConnectionWriter.Advance(bytes);

    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        // No-op. We don't want to dispose the transport connection writer.
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _transportConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The duplex connection can only be disposed if this connection is disposed.
            throw new ConnectionException(ConnectionErrorCode.OperationAborted);
        }
        return default;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _transportConnectionWriter.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _transportConnectionWriter.GetSpan(sizeHint);

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        try
        {
            await _transportConnectionWriter.WriteAsync(
                new ReadOnlySequence<byte>(source), cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The duplex connection can only be disposed if this connection is disposed.
            throw new ConnectionException(ConnectionErrorCode.OperationAborted);
        }
        return default;
    }

    /// <summary>Writes the source to the duplex connection. <paramref name="endStream"/> is ignored
    /// because the duplex connection has no use for it.</summary>
    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        try
        {
            await _transportConnectionWriter.WriteAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The duplex connection can only be disposed if this connection is disposed.
            throw new ConnectionException(ConnectionErrorCode.OperationAborted);
        }
        return default;
    }

    internal IcePayloadPipeWriter(DuplexConnectionWriter transportConnectionWriter) =>
        _transportConnectionWriter = transportConnectionWriter;
}
