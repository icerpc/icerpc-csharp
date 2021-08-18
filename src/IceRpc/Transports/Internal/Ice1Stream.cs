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

        private protected override async ValueTask<ReadOnlyMemory<byte>> ReceiveIce1FrameAsync(
            Ice1FrameType expectedFrameType,
            CancellationToken cancel)
        {
            // Wait to be signaled for the reception of a new frame for this stream
            (Ice1FrameType frameType, ReadOnlyMemory<byte> frame) = await WaitAsync(cancel).ConfigureAwait(false);

            // If the received frame is not the one we expected, throw.
            if (frameType != expectedFrameType)
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

        private protected override Task SendResetFrameAsync(RpcStreamError errorCode) => Task.CompletedTask;

        private protected override Task SendStopSendingFrameAsync(RpcStreamError errorCode) => Task.CompletedTask;
    }
}
