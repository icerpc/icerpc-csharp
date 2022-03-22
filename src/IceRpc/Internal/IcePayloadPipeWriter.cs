// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A stateless pipe writer used for the payload sink of an outgoing request or response. The writer just
    /// writes to the network connection pipe writer. The network connection pipe writer can't be used directly as the
    /// output sink because we don't want the completion of the output sink to complete the network pipe
    /// writer.</summary>
    internal sealed class IcePayloadPipeWriter : ReadOnlySequencePipeWriter
    {
        private readonly SimpleNetworkConnectionWriter _networkConnectionWriter;

        public override void Advance(int bytes) => _networkConnectionWriter.Advance(bytes);

        public override void CancelPendingFlush() => throw new NotSupportedException();

        public override void Complete(Exception? exception = null)
        {
            // No-op. We don't want to dispose the network connection writer.
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            await _networkConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
            return default;
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _networkConnectionWriter.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _networkConnectionWriter.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            await _networkConnectionWriter.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            return default;
        }

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool _,
            CancellationToken cancel)
        {
            await _networkConnectionWriter.WriteAsync(source, cancel).ConfigureAwait(false);
            return default;
        }

        internal IcePayloadPipeWriter(SimpleNetworkConnectionWriter networkConnectionWriter) =>
            _networkConnectionWriter = networkConnectionWriter;
    }
}
