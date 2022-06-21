// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A stateless pipe writer used for the payload writer of an outgoing request or response. The writer just
/// writes to the network connection writer.</summary>
internal sealed class IcePayloadPipeWriter : ReadOnlySequencePipeWriter
{
    private readonly SimpleNetworkConnectionWriter _networkConnectionWriter;

    public override void Advance(int bytes) => _networkConnectionWriter.Advance(bytes);

    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        // No-op. We don't want to dispose the network connection writer.
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancel = default)
    {
        // The cancellation of the flush just cancels the wait for the data writing. It doesn't cancel the write as
        // it would leave the connection is a non-recoverable state.
        try
        {
            await _networkConnectionWriter.FlushAsync(
                CancellationToken.None).AsTask().WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            throw new ConnectionAbortedException();
        }
        return default;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _networkConnectionWriter.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _networkConnectionWriter.GetSpan(sizeHint);

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel)
    {
        // The cancellation of the write just cancels the wait for the data writing. It doesn't cancel the write as
        // it would leave the connection is a non-recoverable state.
        try
        {
            await _networkConnectionWriter.WriteAsync(
                new ReadOnlySequence<byte>(source),
                CancellationToken.None).AsTask().WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            throw new ConnectionAbortedException();
        }
        return default;
    }

    /// <summary>Writes the source to the simple network connection. <paramref name="endStream"/> is ignored because
    /// the simple network connection has no use for it.</summary>
    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancel)
    {
        // The cancellation of the flush just cancels the wait for the data writing. It doesn't cancel the write as
        // it would leave the connection is a non-recoverable state.
        try
        {
            await _networkConnectionWriter.WriteAsync(
                source,
                CancellationToken.None).AsTask().WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            throw new ConnectionAbortedException();
        }
        return default;
    }

    internal IcePayloadPipeWriter(SimpleNetworkConnectionWriter networkConnectionWriter) =>
        _networkConnectionWriter = networkConnectionWriter;
}
