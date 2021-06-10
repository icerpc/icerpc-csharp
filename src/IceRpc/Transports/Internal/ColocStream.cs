// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Stream class for the colocated transport.</summary>
    internal class ColocStream : SignaledStream<(object, bool)>
    {
        protected internal override bool ReceivedEndOfStream => _receivedEndOfStream;
        private bool _receivedEndOfStream;
        private ArraySegment<byte> _receiveSegment;
        private readonly ColocConnection _connection;
        private ChannelWriter<byte[]>? _streamWriter;
        private ChannelReader<byte[]>? _streamReader;

        public override string ToString()
        {
            int requestID = Id % 4 < 2 ? (int)(Id >> 2) + 1 : 0;
            return $"ID = {requestID} {(requestID == 0 ? "oneway" : "twoway")}";
        }

        protected override void AbortWrite(StreamErrorCode errorCode)
        {
            // If the stream is aborted, either because it was reset by the peer or because the connection was
            // aborted, there's no need to send a reset frame.
            if (!IsAborted)
            {
                // Send reset frame
                _ = _connection.SendFrameAsync(this, frame: errorCode, fin: true, CancellationToken.None).AsTask();
            }
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
            _connection.SendFrameAsync(this, frame: channel.Reader, fin: false, cancel: default).AsTask();
        }

        protected override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // If we didn't get the stream reader yet, wait for the peer stream to provide it through the
            // socket channel.
            if (_streamReader == null)
            {
                (object frame, bool fin) = await WaitAsync(cancel).ConfigureAwait(false);
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

        protected override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            if (_streamWriter == null)
            {
                await _connection.SendFrameAsync(this, buffers, endStream, cancel).ConfigureAwait(false);
            }
            else
            {
                if (buffers.Span[0].Length > 0)
                {
                    // TODO: replace the channel with a lightweight asynchronous queue which doesn't require
                    // copying the data from the sender. Copying the data is necessary here because WriteAsync
                    // doesn't block if there's space in the channel and it's not possible to create a
                    // bounded channel with a null capacity.
                    // TODO: why are we copying only the first buffer??
                    byte[] copy = new byte[buffers.Span[0].Length];
                    buffers.Span[0].CopyTo(copy);
                    await _streamWriter.WriteAsync(copy, cancel).ConfigureAwait(false);
                }
                if (endStream)
                {
                    _streamWriter.Complete();
                }
            }
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            _connection.ReleaseStream(this);
        }

        /// <summary>Constructor for incoming colocated stream</summary>
        internal ColocStream(ColocConnection connection, long streamId)
            : base(connection, streamId) => _connection = connection;

        /// <summary>Constructor for outgoing colocated stream</summary>
        internal ColocStream(ColocConnection connection, bool bidirectional, bool control)
            : base(connection, bidirectional, control) => _connection = connection;

        internal void ReceivedFrame(object frame, bool fin)
        {
            if (frame is StreamErrorCode errorCode)
            {
                AbortRead(errorCode);
                CancelDispatchSource?.Cancel();
            }
            else
            {
                QueueResult((frame, fin));
            }
        }

        internal override async ValueTask<IncomingRequest> ReceiveRequestFrameAsync(CancellationToken cancel)
        {
            (object frameObject, bool fin) = await WaitAsync(cancel).ConfigureAwait(false);
            Debug.Assert(frameObject is IncomingRequest);
            var frame = (IncomingRequest)frameObject;

            if (fin)
            {
                _receivedEndOfStream = true;
            }
            else
            {
                frame.Stream = this;
                Interlocked.Increment(ref _useCount);
            }
            return frame;
        }

        internal override async ValueTask<IncomingResponse> ReceiveResponseFrameAsync(CancellationToken cancel)
        {
            (object frameObject, bool fin) = await WaitAsync(cancel).ConfigureAwait(false);
            var frame = (IncomingResponse)frameObject;
            if (fin)
            {
                _receivedEndOfStream = true;
            }
            else
            {
                frame.Stream = this;
                Interlocked.Increment(ref _useCount);
            }
            return frame;
        }

        private protected override async ValueTask<ArraySegment<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel)
        {
            (object frame, bool fin) = await WaitAsync(cancel).ConfigureAwait(false);
            if (fin)
            {
                _receivedEndOfStream = true;
            }

            if (frame is ReadOnlyMemory<ReadOnlyMemory<byte>> data)
            {
                // Initialize or GoAway frame.
                if (_connection.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(expectedFrameType == data.Span[0].Span[8]);
                    return ArraySegment<byte>.Empty;
                }
                else
                {
                    Debug.Assert(expectedFrameType == data.Span[0].Span[0]);
                    (int size, int sizeLength) = data.Span[0].Span[1..].ReadSize20();

                    // temporary
                    if (MemoryMarshal.TryGetArray(data.Span[0].Slice(1 + sizeLength, size),
                                                  out ArraySegment<byte> result))
                    {
                        return result;
                    }
                    else
                    {
                        Debug.Assert(false);
                        return default;
                    }
                }
            }
            else
            {
                Debug.Assert(false);
                throw new InvalidDataException("unexpected frame");
            }
        }

        private protected override async ValueTask SendFrameAsync(OutgoingFrame frame, CancellationToken cancel) =>
            await _connection.SendFrameAsync(
                this,
                frame.ToIncoming(),
                fin: frame.StreamDataWriter == null,
                cancel).ConfigureAwait(false);
    }
}
