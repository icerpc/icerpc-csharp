// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>The SocketStream class for the colocated transport.</summary>
    internal class ColocatedStream : SignaledSocketStream<(object, bool)>
    {
        protected override bool ReceivedEndOfStream => _receivedEndOfStream;
        private bool _receivedEndOfStream;
        private ArraySegment<byte> _receiveSegment;
        private readonly ColocatedSocket _socket;
        private ChannelWriter<byte[]>? _streamWriter;
        private ChannelReader<byte[]>? _streamReader;

        protected override void Destroy()
        {
            base.Destroy();
            _socket.ReleaseStream(this);
        }

        protected override void EnableSendFlowControl()
        {
            base.EnableSendFlowControl();

            // Create a channel to send the data directly to the peer's stream. It's a bounded channel
            // of one element which requires the sender to wait if the channel is full. This ensures
            // that the sender doesn't send the data faster than the receiver can process. Using channels
            // for this purpose might be a little overkill, we could consider adding a small async queue
            // class for this purpose instead.
            var channelOptions = new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            };
            var channel = Channel.CreateBounded<byte[]>(channelOptions);
            _streamWriter = channel.Writer;

            // Send the channel reader to the peer. Receiving data will first wait for the channel reader
            // to be transmitted.
            _socket.SendFrameAsync(this, frame: channel.Reader, fin: false, cancel: default).AsTask();
        }

        protected override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // If we didn't get the stream reader yet, wait for the peer stream to provide it through the
            // socket channel.
            if (_streamReader == null)
            {
                (object frame, bool fin) = await WaitSignalAsync(cancel).ConfigureAwait(false);
                _streamReader = frame as ChannelReader<byte[]>;
                Debug.Assert(_streamReader != null);
            }

            int received = 0;
            while (buffer.Length > 0)
            {
                if (_receiveSegment.Count > 0)
                {
                    if (buffer.Length < _receiveSegment.Count)
                    {
                        _receiveSegment[0..buffer.Length].AsMemory().CopyTo(buffer);
                        received += buffer.Length;
                        _receiveSegment = _receiveSegment[buffer.Length..];
                        buffer = buffer[buffer.Length..];
                    }
                    else
                    {
                        _receiveSegment.AsMemory().CopyTo(buffer);
                        received += _receiveSegment.Count;
                        _receiveSegment = new ArraySegment<byte>();
                        buffer = Memory<byte>.Empty;
                    }
                }
                else
                {
                    if (_receivedEndOfStream)
                    {
                        return 0;
                    }

                    try
                    {
                        _receiveSegment = await _streamReader.ReadAsync(cancel).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    {
                        _receivedEndOfStream = true;
                    }
                }
            }
            return received;
        }

        protected override ValueTask ResetAsync(long errorCode) =>
            // A null frame indicates a stream reset.
            // TODO: Provide the error code?
            _socket.SendFrameAsync(this, frame: null, fin: true, CancellationToken.None);

        protected override async ValueTask SendAsync(
            IList<ArraySegment<byte>> buffer,
            bool fin,
            CancellationToken cancel)
        {
            if (_streamWriter == null)
            {
                await _socket.SendFrameAsync(this, buffer, fin, cancel).ConfigureAwait(false);
            }
            else
            {
                if (buffer[0].Count > 0)
                {
                    // TODO: replace the channel with a better mechanism which doesn't require copying the data
                    // from the sender.
                    byte[] copy = new byte[buffer[0].Count];
                    buffer[0].CopyTo(copy);
                    await _streamWriter.WriteAsync(copy, cancel).ConfigureAwait(false);
                }
                if (fin)
                {
                    _streamWriter.Complete();
                }
            }
        }

        /// <summary>Constructor for incoming colocated stream</summary>
        internal ColocatedStream(ColocatedSocket socket, long streamId)
            : base(socket, streamId) => _socket = socket;

        /// <summary>Constructor for outgoing colocated stream</summary>
        internal ColocatedStream(ColocatedSocket socket, bool bidirectional, bool control)
            : base(socket, bidirectional, control) => _socket = socket;

        internal void ReceivedFrame(object frame, bool fin) => QueueResult((frame, fin));

        internal override async ValueTask<IncomingRequestFrame> ReceiveRequestFrameAsync(CancellationToken cancel)
        {
            (object frameObject, bool fin) = await WaitSignalAsync(cancel).ConfigureAwait(false);
            Debug.Assert(frameObject is IncomingRequestFrame);
            var frame = (IncomingRequestFrame)frameObject;

            if (fin)
            {
                _receivedEndOfStream = true;
            }
            else
            {
                frame.SocketStream = this;
                Interlocked.Increment(ref _useCount);
            }

            if (_socket.Endpoint.Communicator.TraceLevels.Protocol >= 1)
            {
                _socket.TraceFrame(Id, frame);
            }

            return frame;
        }

        internal override async ValueTask<IncomingResponseFrame> ReceiveResponseFrameAsync(CancellationToken cancel)
        {
            object frameObject;
            bool fin;

            try
            {
                (frameObject, fin) = await WaitSignalAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (_socket.Endpoint.Protocol != Protocol.Ice1)
                {
                    await ResetAsync((long)StreamResetErrorCode.RequestCanceled).ConfigureAwait(false);
                }
                throw;
            }

            Debug.Assert(frameObject is IncomingResponseFrame);
            var frame = (IncomingResponseFrame)frameObject;

            if (fin)
            {
                _receivedEndOfStream = true;
            }
            else
            {
                frame.SocketStream = this;
                Interlocked.Increment(ref _useCount);
            }

            if (_socket.Endpoint.Communicator.TraceLevels.Protocol >= 1)
            {
                _socket.TraceFrame(Id, frame);
            }

            return frame;
        }

        private protected override async ValueTask<ArraySegment<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel)
        {
            (object frame, bool fin) = await WaitSignalAsync(cancel).ConfigureAwait(false);
            if (fin)
            {
                _receivedEndOfStream = true;
            }

            if (frame is List<ArraySegment<byte>> data)
            {
                // Initialize or GoAway frame.
                if (_socket.Endpoint.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(expectedFrameType == data[0][8]);
                    return ArraySegment<byte>.Empty;
                }
                else
                {
                    Debug.Assert(expectedFrameType == data[0][0]);
                    (int size, int sizeLength) = data[0][1..].AsReadOnlySpan().ReadSize20();
                    return data[0].Slice(1 + sizeLength, size);
                }
            }
            else
            {
                Debug.Assert(false);
                throw new InvalidDataException("unexpected frame");
            }
        }

        private protected override async ValueTask SendFrameAsync(OutgoingFrame frame, CancellationToken cancel)
        {
            await _socket.SendFrameAsync(this, frame.ToIncoming(), fin: frame.StreamDataWriter == null, cancel).
                ConfigureAwait(false);

            if (_socket.Endpoint.Communicator.TraceLevels.Protocol >= 1)
            {
                TraceFrame(frame);
            }
        }
    }
}
