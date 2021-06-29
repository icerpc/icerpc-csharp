// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>The RpcStream class for the colocated transport.</summary>
    internal class ColocStream : SignaledStream<(object, bool)>
    {
        private Memory<byte> _receiveBuffer;
        private readonly ColocConnection _connection;
        private ChannelWriter<byte[]>? _streamWriter;
        private ChannelReader<byte[]>? _streamReader;

        public override void AbortRead(RpcStreamError errorCode)
        {
            if (TrySetReadCompleted(shutdown: false))
            {
                // Abort the receive call waiting on WaitAsync().
                SetException(new RpcStreamAbortedException(errorCode));

                // Send stop sending frame before shutting down.
                // TODO

                // Shutdown the stream if not already done.
                TryShutdown();
            }
        }

        public override void AbortWrite(RpcStreamError errorCode)
        {
            // Notify the peer of the abort if the stream or connection is not aborted already.
            if (!IsShutdown && errorCode != RpcStreamError.ConnectionAborted)
            {
                _ = _connection.SendFrameAsync(this, frame: errorCode, fin: true, CancellationToken.None).AsTask();
            }

            if (TrySetWriteCompleted(shutdown: false))
            {
                // Ensure further SendAsync calls raise StreamAbortException
                SetException(new RpcStreamAbortedException(errorCode));

                // Shutdown the stream if not already done.
                TryShutdown();
            }
        }

        public override void EnableReceiveFlowControl()
        {
            // Nothing to do.
        }

        public override void EnableSendFlowControl()
        {
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

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
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
                if (_receiveBuffer.Length > 0)
                {
                    if (buffer.Length < _receiveBuffer.Length)
                    {
                        _receiveBuffer[0..buffer.Length].CopyTo(buffer);
                        received += buffer.Length;
                        _receiveBuffer = _receiveBuffer[buffer.Length..];
                        buffer = buffer[buffer.Length..];
                    }
                    else
                    {
                        _receiveBuffer.CopyTo(buffer);
                        received += _receiveBuffer.Length;
                        _receiveBuffer = Memory<byte>.Empty;
                        buffer = Memory<byte>.Empty;
                    }
                }
                else
                {
                    if (ReadCompleted)
                    {
                        return 0;
                    }

                    try
                    {
                        _receiveBuffer = await _streamReader.ReadAsync(cancel).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    {
                        TrySetReadCompleted();
                    }
                }
            }
            return received;
        }

        public override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            if (WriteCompleted)
            {
                throw new RpcStreamAbortedException(RpcStreamError.StreamAborted);
            }

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

            if (endStream)
            {
                TrySetWriteCompleted();
            }
        }

        public override string ToString()
        {
            int requestID = Id % 4 < 2 ? (int)(Id >> 2) + 1 : 0;
            return $"ID = {requestID} {(requestID == 0 ? "oneway" : "twoway")}";
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
            if (frame is RpcStreamError errorCode)
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
            if (ReadCompleted || (fin && !TrySetReadCompleted()))
            {
                throw AbortException ?? new InvalidOperationException("stream receive is completed");
            }

            Debug.Assert(frameObject is IncomingRequest);
            var frame = (IncomingRequest)frameObject;
            return frame;
        }

        internal override async ValueTask<IncomingResponse> ReceiveResponseFrameAsync(CancellationToken cancel)
        {
            (object frameObject, bool fin) = await WaitAsync(cancel).ConfigureAwait(false);
            if (ReadCompleted || (fin && !TrySetReadCompleted()))
            {
                throw AbortException ?? new InvalidOperationException("stream receive is completed");
            }
            return (IncomingResponse)frameObject;
        }

        private protected override async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel)
        {
            (object frame, bool fin) = await WaitAsync(cancel).ConfigureAwait(false);
            if (ReadCompleted || (fin && !TrySetReadCompleted()))
            {
                throw AbortException ?? new InvalidOperationException("stream receive is completed");
            }

            if (frame is ReadOnlyMemory<ReadOnlyMemory<byte>> data)
            {
                // Initialize or GoAway frame.
                if (_connection.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(expectedFrameType == data.Span[0].Span[8]);
                    return Memory<byte>.Empty;
                }
                else
                {
                    Debug.Assert(expectedFrameType == data.Span[0].Span[0]);
                    (int size, int sizeLength) = data.Span[0].Span[1..].ReadSize20();

                    // TODO: why are we returning only the first buffer?
                    return data.Span[0].Slice(1 + sizeLength, size);
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
                fin: frame.StreamWriter == null,
                cancel).ConfigureAwait(false);
    }
}
