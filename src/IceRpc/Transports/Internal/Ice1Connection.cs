// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Ice1 connection implements a multi-stream connection for the Ice1 protocol. A new incoming
    /// stream is created for each incoming Ice1 request and an outgoing stream is created for outgoing requests.
    /// The streams created by the Ice1 connection are always finished once the request or response frames are
    /// sent or received. Data streaming is not supported. Initialize or GoAway frames sent over the control streams
    /// are translated to connection validation or close connection Ice1 frames.</summary>
    internal class Ice1Connection : NetworkSocketConnection
    {
        internal bool IsValidated { get; private set; }

        private readonly AsyncSemaphore? _bidirectionalStreamSemaphore;
        // The mutex is used to protect the next stream IDs and the send queue.
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private long _nextPeerUnidirectionalId;
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private readonly AsyncSemaphore? _unidirectionalStreamSemaphore;

        public override async ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel)
        {
            while (true)
            {
                // Receive the Ice1 frame header.
                Memory<byte> buffer;
                if (IsDatagram)
                {
                    buffer = new byte[NetworkSocket.DatagramMaxReceiveSize];
                    int received = await NetworkSocket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                    if (received < Ice1Definitions.HeaderSize)
                    {
                        Logger.LogReceivedInvalidDatagram(received);
                        continue; // while
                    }

                    buffer = buffer[0..received];
                    Received(buffer);
                }
                else
                {
                    buffer = new byte[256];
                    await ReceiveUntilFullAsync(buffer[0..Ice1Definitions.HeaderSize], cancel).ConfigureAwait(false);
                }

                // Check the header
                Ice1Definitions.CheckHeader(buffer.Span.Slice(0, Ice1Definitions.HeaderSize));
                int frameSize = IceDecoder.DecodeInt(buffer.AsReadOnlySpan().Slice(10, 4));
                if (frameSize < Ice1Definitions.HeaderSize)
                {
                    if (IsDatagram)
                    {
                        Logger.LogReceivedInvalidDatagram(frameSize);
                    }
                    else
                    {
                        throw new InvalidDataException($"received ice1 frame with only {frameSize} bytes");
                    }
                    continue; // while
                }
                if (frameSize > IncomingFrameMaxSize)
                {
                    if (IsDatagram)
                    {
                        Logger.LogDatagramSizeExceededIncomingFrameMaxSize(frameSize);
                        continue;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"frame with {frameSize} bytes exceeds IncomingFrameMaxSize connection option value");
                    }
                }

                // Read the remainder of the frame if needed.
                if (frameSize > buffer.Length)
                {
                    if (IsDatagram)
                    {
                        Logger.LogDatagramMaximumSizeExceeded(frameSize);
                        continue;
                    }

                    Memory<byte> newBuffer = new byte[frameSize];
                    buffer[0..Ice1Definitions.HeaderSize].CopyTo(newBuffer[0..Ice1Definitions.HeaderSize]);
                    buffer = newBuffer;
                }
                else if (!IsDatagram)
                {
                    buffer = buffer[0..frameSize];
                }

                if (!IsDatagram && frameSize > Ice1Definitions.HeaderSize)
                {
                    await ReceiveUntilFullAsync(buffer[Ice1Definitions.HeaderSize..], cancel).ConfigureAwait(false);
                }

                // Make sure the connection is marked as validated. This flag is necessary because incoming
                // connection initialization doesn't wait for connection validation message. So the connection
                // is considered validated on the server side only once the first frame is received. This is
                // only useful for connection warnings, to prevent a warning from showing up if the server side
                // connection is closed before the first message is received (which can occur with SSL for
                // example if the certification validation fails on the client side).
                IsValidated = true;

                // Parse the received frame and translate it into a stream ID, frame type and frame data. The returned
                // stream ID can be negative if the Ice1 frame is no longer supported (batch requests).
                (long streamId, Ice1FrameType frameType, ReadOnlyMemory<byte> frame) = ParseFrame(buffer);
                if (streamId >= 0)
                {
                    if (TryGetStream(streamId, out Ice1Stream? stream))
                    {
                        // If this is a known stream, pass the data to the stream.
                        if (frameType == Ice1FrameType.ValidateConnection)
                        {
                            // Except for the validate connection frame, subsequent validate connection messages are
                            // heartbeats sent by the peer. We just handle it here and don't pass it over the control
                            // stream which only expect the close frame at this point.
                            Debug.Assert(stream.IsControl);
                            continue;
                        }
                        else if (frameType == Ice1FrameType.CloseConnection && IsDatagram)
                        {
                            Debug.Assert(stream.IsControl);
                            Logger.LogDatagramConnectionReceiveCloseConnectionFrame();
                            continue;
                        }

                        try
                        {
                            stream.ReceivedFrame(frameType, frame);
                        }
                        catch
                        {
                            // Ignore, the stream has been aborted
                        }
                    }
                    else if (frameType == Ice1FrameType.Request)
                    {
                        // Create a new stream for the request. If serialization is enabled, ensure we acquire
                        // the semaphore first to serialize the dispatching.
                        try
                        {
                            stream = new Ice1Stream(this, streamId);
                            AsyncSemaphore? semaphore = stream.IsBidirectional ?
                                _bidirectionalStreamSemaphore : _unidirectionalStreamSemaphore;
                            if (semaphore != null)
                            {
                                await semaphore.EnterAsync(cancel).ConfigureAwait(false);
                            }
                            stream.ReceivedFrame(frameType, frame);
                            return stream;
                        }
                        catch
                        {
                            // Ignore, if the stream has been aborted or the connection is being shutdown.
                        }
                    }
                    else if (frameType == Ice1FrameType.ValidateConnection)
                    {
                        // If we received a connection validation frame and the stream is not known, it's the first
                        // received connection validation message, create the control stream and return it.
                        stream = new Ice1Stream(this, streamId);
                        Debug.Assert(stream.IsControl);
                        stream.ReceivedFrame(frameType, frame);
                        return stream;
                    }
                    else
                    {
                        // The stream has been disposed, ignore the data.
                    }
                }
            }
        }

        public override RpcStream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new Ice1Stream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public override ValueTask InitializeAsync(CancellationToken cancel) => default;

        public override async Task PingAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            await SendFrameAsync(null, Ice1Definitions.ValidateConnectionFrame, cancel).ConfigureAwait(false);

            Logger.LogSentInitializeFrame(this, 0);
        }

        internal Ice1Connection(
            NetworkSocket networkSocket,
            Endpoint endpoint,
            bool isServer,
            MultiStreamOptions options)
            : base(networkSocket, endpoint, isServer)
        {
            // Create semaphore to limit the number of concurrent dispatch per connection on the server-side.
            _bidirectionalStreamSemaphore = new AsyncSemaphore(options.BidirectionalStreamMaxCount);
            _unidirectionalStreamSemaphore = new AsyncSemaphore(options.UnidirectionalStreamMaxCount);

            // We use the same stream ID numbering scheme as Quic.
            if (IsServer)
            {
                _nextBidirectionalId = 1;
                _nextUnidirectionalId = 3;
                _nextPeerUnidirectionalId = 2;
            }
            else
            {
                _nextBidirectionalId = 0;
                _nextUnidirectionalId = 2;
                _nextPeerUnidirectionalId = 3;
            }
        }

        internal override ValueTask<RpcStream> ReceiveInitializeFrameAsync(CancellationToken cancel)
        {
            // With Ice1, the connection validation message is only sent by the server to the client. So here we
            // only expect the connection validation message for a client connection and just return the
            // control stream immediately for a server connection.
            if (IsServer)
            {
                return new ValueTask<RpcStream>(new Ice1Stream(this, 2));
            }
            else
            {
                return base.ReceiveInitializeFrameAsync(cancel);
            }
        }

        internal void ReleaseStream(Ice1Stream stream)
        {
            if (stream.IsIncoming && !stream.IsControl)
            {
                if (stream.IsBidirectional)
                {
                    _bidirectionalStreamSemaphore?.Release();
                }
                else
                {
                    _unidirectionalStreamSemaphore?.Release();
                }
            }
        }

        internal async ValueTask SendFrameAsync(
            Ice1Stream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue
            // to serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            // If the stream is aborted, stop sending stream frames.
            if (stream?.WriteCompleted ?? false)
            {
                _sendSemaphore.Release();
                throw new RpcStreamAbortedException(RpcStreamError.StreamAborted);
            }

            try
            {
                // If the stream is not started, assign the stream ID now. If we're sending a request, also make sure
                // to set the request ID in the header.
                if (stream != null && !stream.IsStarted)
                {
                    stream.Id = AllocateId(stream.IsBidirectional);
                    if (buffers.Span[0].Span[8] == (byte)Ice1FrameType.Request)
                    {
                        Memory<byte> requestIdBuffer =
                            MemoryMarshal.AsMemory(buffers.Span[0][Ice1Definitions.HeaderSize..]);

                        IceEncoder.EncodeInt(stream.RequestId, requestIdBuffer.Span);
                    }
                }

                // Perform the sending.
                // When an an Ice1 frame is sent over a connection (such as a TCP connection), we need to send the
                // entire frame even when cancel gets canceled since the recipient cannot read a partial frame and then
                // keep going.
                await NetworkSocket.SendAsync(
                    buffers,
                    IsDatagram ? cancel : CancellationToken.None).ConfigureAwait(false);

                Sent(buffers);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        internal override ValueTask<RpcStream> SendInitializeFrameAsync(CancellationToken cancel)
        {
            // With Ice1, the connection validation message is only sent by the server to the client. So here
            // we only expect the connection validation message for a server connection and just return the
            // control stream immediately for a client connection.
            if (IsServer)
            {
                return base.SendInitializeFrameAsync(cancel);
            }
            else
            {
                return new ValueTask<RpcStream>(new Ice1Stream(this, AllocateId(false)));
            }
        }

        private long AllocateId(bool bidirectional)
        {
            // Allocate a new ID according to the Quic numbering scheme.
            long id;
            if (bidirectional)
            {
                id = _nextBidirectionalId;
                _nextBidirectionalId += 4;
            }
            else
            {
                id = _nextUnidirectionalId;
                _nextUnidirectionalId += 4;
            }
            return id;
        }

        private (long, Ice1FrameType, ReadOnlyMemory<byte>) ParseFrame(ReadOnlyMemory<byte> readBuffer)
        {
            // The magic and version fields have already been checked.
            var frameType = (Ice1FrameType)readBuffer.Span[8];
            byte compressionStatus = readBuffer.Span[9];
            if (compressionStatus == 2)
            {
                throw new NotSupportedException("cannot decompress ice1 frame");
            }

            switch (frameType)
            {
                case Ice1FrameType.CloseConnection:
                {
                    return (IsServer ? 2 : 3, frameType, default);
                }

                case Ice1FrameType.Request:
                {
                    int requestId = IceDecoder.DecodeInt(readBuffer.Span.Slice(Ice1Definitions.HeaderSize, 4));

                    // Compute the stream ID out of the request ID. For one-way requests which use a null request ID,
                    // we generate a new stream ID using the _nextPeerUnidirectionalId counter.
                    long streamId;
                    if (requestId == 0)
                    {
                        streamId = _nextPeerUnidirectionalId += 4;
                    }
                    else
                    {
                        streamId = ((requestId - 1) << 2) + (IsServer ? 0 : 1);
                    }
                    return (streamId, frameType, readBuffer[(Ice1Definitions.HeaderSize + 4)..]);
                }

                case Ice1FrameType.RequestBatch:
                {
                    int invokeNum = IceDecoder.DecodeInt(readBuffer.Span.Slice(Ice1Definitions.HeaderSize, 4));
                    Logger.LogReceivedIce1RequestBatchFrame(invokeNum);

                    if (invokeNum < 0)
                    {
                        throw new InvalidDataException(
                            $"received ice1 RequestBatchMessage with {invokeNum} batch requests");
                    }
                    return (-1, frameType, default);
                }

                case Ice1FrameType.Reply:
                {
                    int requestId = IceDecoder.DecodeInt(readBuffer.Span.Slice(Ice1Definitions.HeaderSize, 4));
                    long streamId = ((requestId - 1) << 2) + (IsServer ? 1 : 0);
                    return (streamId, frameType, readBuffer[(Ice1Definitions.HeaderSize + 4)..]);
                }

                case Ice1FrameType.ValidateConnection:
                {
                    // Notify the control stream of the reception of a Ping frame.
                    PingReceived?.Invoke();
                    return (IsServer ? 2 : 3, frameType, default);
                }

                default:
                {
                    throw new InvalidDataException($"received ice1 frame with unknown frame type '{frameType}'");
                }
            }
        }

        private async ValueTask ReceiveUntilFullAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int offset = 0;
            while (offset != buffer.Length)
            {
                offset += await NetworkSocket.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
            }
            Received(buffer);
        }
    }
}
