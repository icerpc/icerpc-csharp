// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A stateless pipe writer used for the payload sink of an outgoing request or response. The writer just
    /// the writting to the network connection pipe writer. The network connection pipe writer can't be used directly
    /// as the output sink because we don't want the completion of the output sink to complete the the network pipe
    /// writer.</summary>
    internal sealed class IcePayloadPipeWriter : PipeWriter
    {
        private readonly PipeWriter _networkConnectionWriter;

        public override void Advance(int bytes) => _networkConnectionWriter.Advance(bytes);

        public override void CancelPendingFlush() => _networkConnectionWriter.CancelPendingFlush();

        public override void Complete(Exception? exception = null)
        {
            // No-op. We don't want to close the network connection pipe writer.
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
            _networkConnectionWriter.FlushAsync(cancellationToken);

        public override Memory<byte> GetMemory(int sizeHint = 0) => _networkConnectionWriter.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _networkConnectionWriter.GetSpan(sizeHint);

        internal IcePayloadPipeWriter(PipeWriter networkConnectionWriter) =>
            _networkConnectionWriter = networkConnectionWriter;
    }
}
