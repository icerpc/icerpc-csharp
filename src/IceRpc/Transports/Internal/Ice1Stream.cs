// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Ice1Stream class implements RpcStream.</summary>
    internal class Ice1Stream : SignaledStream<(Ice1FrameType, ReadOnlyMemory<byte>)>
    {
        internal int RequestId => IsBidirectional ? ((int)(Id >> 2) + 1) : 0;
        private readonly Ice1Connection _connection;

        public override void EnableReceiveFlowControl() =>
            // This is never called because streaming isn't supported with Ice1.
            throw new NotImplementedException();

        public override void EnableSendFlowControl() =>
            // This is never called because streaming isn't supported with Ice1.
            throw new NotImplementedException();

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            // This is never called because we override the default ReceiveFrameAsync implementation.
            throw new NotImplementedException();

        public override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            // This method is used for sending validation connection and close connection messages on the control
            // stream. It's not used for sending requests/responses, SendFrameAsync is used instead.
            Debug.Assert(IsControl);
            await _connection.SendFrameAsync(this, buffers, cancel).ConfigureAwait(false);
            if (endStream)
            {
                TrySetWriteCompleted();
            }
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            _connection.ReleaseStream(this);
        }

        internal Ice1Stream(Ice1Connection connection, long streamId)
            : base(connection, streamId) => _connection = connection;

        internal Ice1Stream(Ice1Connection connection, bool bidirectional, bool control)
            : base(connection, bidirectional, control) => _connection = connection;

        internal void ReceivedFrame(Ice1FrameType frameType, ReadOnlyMemory<byte> frame)
        {
            if (frameType == Ice1FrameType.Reply && _connection.LastResponseStreamId < Id)
            {
                _connection.LastResponseStreamId = Id;
            }

            SetResult((frameType, frame));
        }

        private protected override async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel)
        {
            // Wait to be signaled for the reception of a new frame for this stream
            (Ice1FrameType frameType, ReadOnlyMemory<byte> frame) = await WaitAsync(cancel).ConfigureAwait(false);

            // If the received frame is not the one we expected, throw.
            if ((byte)frameType != expectedFrameType)
            {
                throw new InvalidDataException($"received frame type {frameType} but expected {expectedFrameType}");
            }

            // An Ice1 stream can only receive a single frame, except if it's a control stream which can
            // receive multiple connection validation messages.
            if (!IsControl)
            {
                TrySetReadCompleted();
            }

            return frame;
        }

        private protected override async ValueTask SendFrameAsync(OutgoingFrame frame, CancellationToken cancel)
        {
            if (frame.StreamParamSender != null)
            {
                throw new NotSupportedException("stream parameters are not supported with ice1");
            }

            var bufferWriter = new BufferWriter();
            var encoder = new IceEncoder(Encoding.Ice11, bufferWriter);
            bufferWriter.WriteByteSpan(Ice1Definitions.FramePrologue);
            encoder.EncodeIce1FrameType(frame is OutgoingRequest ? Ice1FrameType.Request : Ice1FrameType.Reply);
            encoder.EncodeByte(0); // compression status
            BufferWriter.Position start = encoder.StartFixedLengthSize();

            // Note: we don't write the request ID here if the stream ID is not allocated yet. We want to allocate
            // it from the send queue to ensure requests are sent in the same order as the request ID values.
            encoder.EncodeInt(IsStarted ? RequestId : 0);
            frame.EncodeHeader(encoder);

            encoder.EncodeFixedLengthSize11(bufferWriter.Size + frame.PayloadSize, start); // frame size

            // Coalesce small payload buffers at the end of the current header buffer
            int payloadIndex = 0;
            while (payloadIndex < frame.Payload.Length &&
                   frame.Payload.Span[payloadIndex].Length <= bufferWriter.Capacity - bufferWriter.Size)
            {
                bufferWriter.WriteByteSpan(frame.Payload.Span[payloadIndex++].Span);
            }

            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = bufferWriter.Finish(); // only headers so far

            if (payloadIndex < frame.Payload.Length)
            {
                // Need to append the remaining payload buffers
                var newBuffers = new ReadOnlyMemory<byte>[buffers.Length + frame.Payload.Length - payloadIndex];
                buffers.CopyTo(newBuffers);
                frame.Payload[payloadIndex..].CopyTo(newBuffers.AsMemory(buffers.Length));
                buffers = newBuffers;
            }

            await _connection.SendFrameAsync(this, buffers, cancel).ConfigureAwait(false);

            TrySetWriteCompleted();
        }

        private protected override Task SendResetFrameAsync(RpcStreamError errorCode) => Task.CompletedTask;

        private protected override Task SendStopSendingFrameAsync(RpcStreamError errorCode) => Task.CompletedTask;
    }
}
