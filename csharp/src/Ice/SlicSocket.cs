// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ZeroC.Ice.Slic;

namespace ZeroC.Ice
{
    /// <summary>The Slic socket implements a multi-stream transport on top of a single-stream transport such
    /// as TCP. It supports the same set of features as Quic.</summary>
    internal class SlicSocket : MultiStreamOverSingleStreamSocket
    {
        public override TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            internal set => throw new NotSupportedException("setting IdleTimeout is not supported with Slic");
        }
        internal int PeerPacketMaxSize { get; private set; }
        internal int PeerStreamBufferMaxSize { get; private set; }

        private int _bidirectionalStreamCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private TimeSpan _idleTimeout;
        private long _lastBidirectionalId;
        private long _lastUnidirectionalId;
        private readonly int _maxBidirectionalStreams;
        private readonly int _maxUnidirectionalStreams;
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private readonly ManualResetValueTaskCompletionSource<int> _receiveStreamCompletionTaskSource = new();
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private readonly BufferedReceiveOverSingleStreamSocket _socket;
        private Memory<byte>? _streamConsumedBuffer;
        private int _unidirectionalStreamCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;

        public override async ValueTask<SocketStream> AcceptStreamAsync(CancellationToken cancel)
        {
            // Eventually wait for the stream data receive to complete if stream data is being received.
            await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);

            while (true)
            {
                // Read the Slic frame header
                (SlicDefinitions.FrameType type, int size, long? streamId) =
                    await ReceiveHeaderAsync(cancel).ConfigureAwait(false);

                switch (type)
                {
                    case SlicDefinitions.FrameType.Ping:
                    {
                        if (size != 0)
                        {
                            throw new InvalidDataException("unexpected data for Slic Ping fame");
                        }
                        _ = PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Pong, cancel: CancellationToken.None);
                        ReceivedPing();
                        break;
                    }
                    case SlicDefinitions.FrameType.Pong:
                    {
                        // TODO: setup and reset timer here for the pong frame response?
                        if (size != 0)
                        {
                            throw new InvalidDataException("unexpected data for Slic Pong fame");
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.Stream:
                    case SlicDefinitions.FrameType.StreamLast:
                    {
                        Debug.Assert(streamId != null);
                        bool isIncoming = streamId.Value % 2 == (IsIncoming ? 0 : 1);
                        bool isBidirectional = streamId.Value % 4 < 2;
                        bool fin = type == SlicDefinitions.FrameType.StreamLast;

                        if (size == 0 && type == SlicDefinitions.FrameType.Stream)
                        {
                            throw new InvalidDataException("received empty stream frame");
                        }

                        if (TryGetStream(streamId.Value, out SlicStream? stream))
                        {
                            // Notify the stream that data is available for read.
                            stream.ReceivedFrame(size, fin);

                            // Wait for the stream to receive the data before reading a new Slic frame.
                            await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);
                        }
                        else if (isIncoming &&
                                 streamId.Value > (isBidirectional ? _lastBidirectionalId : _lastUnidirectionalId))
                        {
                            // Create a new stream if it's an incoming stream and if it's larger than the last known
                            // stream ID (the client could be sending frames for old canceled incoming streams).

                            // Keep track of the last accepted stream ID.
                            if (isBidirectional)
                            {
                                _lastBidirectionalId = streamId.Value;
                            }
                            else
                            {
                                _lastUnidirectionalId = streamId.Value;
                            }

                            if (size == 0)
                            {
                                throw new InvalidDataException("received empty stream frame on new stream");
                            }

                            // Accept the new incoming stream and notify the stream that data is available.
                            try
                            {
                                stream = new SlicStream(this, streamId.Value);
                            }
                            catch
                            {
                                // The socket is being closed,  we make sure to receive the frame data. If the
                                // connection is being closed gracefully, the connection waits for the socket
                                // to receive the RST from the peer so it's important to receive and skip all
                                // the data until the RST is received.
                                FinishedReceivedStreamData(size);
                                throw;
                            }

                            if (stream.IsControl)
                            {
                                // We don't acquire stream count for the control stream.
                            }
                            else if (isBidirectional)
                            {
                                if (_bidirectionalStreamCount == _maxBidirectionalStreams)
                                {
                                    throw new InvalidDataException(
                                        $"maximum bidirectional stream count {_maxBidirectionalStreams} reached");
                                }
                                Interlocked.Increment(ref _bidirectionalStreamCount);
                            }
                            else
                            {
                                if (_unidirectionalStreamCount == _maxUnidirectionalStreams)
                                {
                                    throw new InvalidDataException(
                                        $"maximum unidirectional stream count {_maxUnidirectionalStreams} reached");
                                }
                                Interlocked.Increment(ref _unidirectionalStreamCount);
                            }
                            stream.ReceivedFrame(size, fin);
                            return stream;
                        }
                        else
                        {
                            if (!isIncoming && fin)
                            {
                                // Release the stream count for the destroyed stream.
                                if (isBidirectional)
                                {
                                    _bidirectionalStreamSemaphore!.Release();
                                }
                                else
                                {
                                    _unidirectionalStreamSemaphore!.Release();
                                }
                            }

                            // The stream has been destroyed, read and ignore the data.
                            if (size > 0)
                            {
                                var receiveBuffer = new ArraySegment<byte>(ArrayPool<byte>.Shared.Rent(size), 0, size);
                                try
                                {
                                    await ReceiveDataAsync(receiveBuffer, cancel).ConfigureAwait(false);
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(receiveBuffer.Array!);
                                }
                            }
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamReset:
                    {
                        Debug.Assert(streamId != null);
                        if (streamId == 2 || streamId == 3)
                        {
                            throw new InvalidDataException("can't reset control streams");
                        }

                        ArraySegment<byte> data = new byte[size];
                        await ReceiveDataAsync(data, cancel).ConfigureAwait(false);

                        var istr = new InputStream(data, SlicDefinitions.Encoding);
                        var streamReset = new StreamResetBody(istr);
                        if (TryGetStream(streamId.Value, out SlicStream? stream))
                        {
                            stream.ReceivedReset((long)streamReset.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamConsumed:
                    {
                        Debug.Assert(streamId != null);
                        if (streamId == 2 || streamId == 3)
                        {
                            throw new InvalidDataException("control streams don't support flow control");
                        }
                        if (size > 8)
                        {
                            throw new InvalidDataException("stream consumed frame too large");
                        }

                        _streamConsumedBuffer ??= new byte[8];

                        await ReceiveDataAsync(_streamConsumedBuffer.Value[0..size], cancel).ConfigureAwait(false);

                        var istr = new InputStream(_streamConsumedBuffer.Value[0..size], SlicDefinitions.Encoding);
                        var streamConsumed = new StreamConsumedBody(istr);
                        if (TryGetStream(streamId.Value, out SlicStream? stream))
                        {
                            stream.ReceivedConsumed((int)streamConsumed.Size);
                        }
                        break;
                    }
                    default:
                    {
                        throw new InvalidDataException($"unexpected Slic frame with frame type `{type}'");
                    }
                }
            }
        }

        public override ValueTask CloseAsync(Exception exception, CancellationToken cancel) =>
            _socket.CloseAsync(exception, cancel);

        public override SocketStream CreateStream(bool bidirectional, bool control) =>
            new SlicStream(this, bidirectional, control);

        public override async ValueTask InitializeAsync(CancellationToken cancel)
        {
            // Initialize the underlying transport
            await _socket.InitializeAsync(cancel).ConfigureAwait(false);

            if (IsIncoming)
            {
                (SlicDefinitions.FrameType type, ArraySegment<byte> data) =
                    await ReceiveFrameAsync(cancel).ConfigureAwait(false);
                if (type != SlicDefinitions.FrameType.Initialize)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type `{type}'");
                }

                // Check that the Slic version is supported (we only support version 1 for now)
                var istr = new InputStream(data, SlicDefinitions.Encoding);
                var initializeBody = new InitializeHeaderBody(istr);

                if (initializeBody.SlicVersion != 1)
                {
                    // If unsupported Slic version, we stop reading there and reply with a Version frame to provide
                    // the client the supported Slic versions.
                    await PrepareAndSendFrameAsync(
                        SlicDefinitions.FrameType.Version,
                        ostr =>
                        {
                            var versionBody = new VersionBody(new uint[] { 1 });
                            versionBody.IceWrite(ostr);
                        },
                        cancel: cancel).ConfigureAwait(false);

                    (type, data) = await ReceiveFrameAsync(cancel).ConfigureAwait(false);
                    if (type != SlicDefinitions.FrameType.Initialize)
                    {
                        throw new InvalidDataException($"unexpected Slic frame with frame type `{type}'");
                    }

                    istr = new InputStream(data, SlicDefinitions.Encoding);
                    initializeBody = new InitializeHeaderBody(istr);
                    if (initializeBody.SlicVersion != 1)
                    {
                        throw new InvalidDataException($"unsupported Slic version `{initializeBody.SlicVersion}'");
                    }
                }

                try
                {
                    if (ProtocolExtensions.Parse(initializeBody.ApplicationProtocolName) != Protocol.Ice2)
                    {
                        throw new NotSupportedException(
                            $"application protocol `{initializeBody.ApplicationProtocolName}' is not supported");
                    }
                }
                catch (FormatException ex)
                {
                    throw new NotSupportedException(
                        $"unknown application protocol `{initializeBody.ApplicationProtocolName}'", ex);
                }

                // Read transport parameters
                ReadParameters(istr);

                // Send back an InitializeAck frame.
                await PrepareAndSendFrameAsync(
                    SlicDefinitions.FrameType.InitializeAck,
                    WriteParameters,
                    cancel: cancel).ConfigureAwait(false);
            }
            else
            {
                // Send the Initialize frame.
                await PrepareAndSendFrameAsync(
                    SlicDefinitions.FrameType.Initialize,
                    ostr =>
                    {
                        var initializeBody = new InitializeHeaderBody(1, Protocol.Ice2.GetName());
                        initializeBody.IceWrite(ostr);
                        WriteParameters(ostr);
                    },
                    cancel: cancel).ConfigureAwait(false);

                // Read the InitializeAck or Version frame from the server
                (SlicDefinitions.FrameType type, ArraySegment<byte> data) =
                    await ReceiveFrameAsync(cancel).ConfigureAwait(false);

                var istr = new InputStream(data, SlicDefinitions.Encoding);

                // If we receive a Version frame, there isn't much we can do as we only support V1 so we throw
                // with an appropriate message to abort the connection.
                if (type == SlicDefinitions.FrameType.Version)
                {
                    // Read the version sequence provided by the server.
                    var versionBody = new VersionBody(istr);
                    throw new InvalidDataException(
                        $"unsupported Slic version, server supports Slic `{string.Join(", ", versionBody.Versions)}'");
                }
                else if (type != SlicDefinitions.FrameType.InitializeAck)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type `{type}'");
                }

                // Read transport parameters
                ReadParameters(istr);
            }
        }

        public override Task PingAsync(CancellationToken cancel) =>
            // TODO: shall we set a timer for expecting the Pong frame?
            PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Ping, cancel: cancel);

        internal SlicSocket(
            SingleStreamSocket socket,
            Endpoint endpoint,
            ObjectAdapter? adapter)
            : base(endpoint, adapter, socket)
        {
            _idleTimeout = endpoint.Communicator.IdleTimeout;
            _socket = new BufferedReceiveOverSingleStreamSocket(socket);
            _receiveStreamCompletionTaskSource.RunContinuationAsynchronously = true;
            _receiveStreamCompletionTaskSource.SetResult(0);

            // Initially set the peer packet max size to the local max size to ensure we can receive the first
            // initialize frame.
            PeerPacketMaxSize = endpoint.Communicator.SlicPacketMaxSize;
            PeerStreamBufferMaxSize = endpoint.Communicator.SlicStreamBufferMaxSize;

            // If serialization is enabled on the adapter, we configure the maximum stream counts to 1 to ensure
            // the peer won't open more than one stream.
            bool serializeDispatch = adapter?.SerializeDispatch ?? false;
            _maxBidirectionalStreams = serializeDispatch ? 1 : endpoint.Communicator.MaxBidirectionalStreams;
            _maxUnidirectionalStreams = serializeDispatch ? 1 : endpoint.Communicator.MaxUnidirectionalStreams;

            // We use the same stream ID numbering scheme as Quic
            if (IsIncoming)
            {
                _nextBidirectionalId = 1;
                _nextUnidirectionalId = 3;
            }
            else
            {
                _nextBidirectionalId = 0;
                _nextUnidirectionalId = 2;
            }
            _lastBidirectionalId = -1;
            _lastUnidirectionalId = -1;
        }

        internal override (long, long) AbortStreams(Exception exception, Func<SocketStream, bool>? predicate)
        {
            (long, long) streamIds = base.AbortStreams(exception, predicate);

            // Unblock requests waiting on the semaphores.
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);

            return streamIds;
        }

        internal void FinishedReceivedStreamData(int remainingSize) =>
            _receiveStreamCompletionTaskSource.SetResult(remainingSize);

        internal async Task PrepareAndSendFrameAsync(
            SlicDefinitions.FrameType type,
            Action<OutputStream>? writer = null,
            long? streamId = null,
            CancellationToken cancel = default)
        {
            var data = new List<ArraySegment<byte>>();
            var ostr = new OutputStream(SlicDefinitions.Encoding, data);
            ostr.WriteByte((byte)type);
            OutputStream.Position sizePos = ostr.StartFixedLengthSize(4);
            if (streamId != null)
            {
                ostr.WriteVarULong((ulong)streamId);
            }
            writer?.Invoke(ostr);
            int frameSize = ostr.Tail.Offset - sizePos.Offset - 4;
            ostr.EndFixedLengthSize(sizePos, 4);
            ostr.Finish();

            if (Endpoint.Communicator.TraceLevels.Transport > 2)
            {
                TraceTransportFrame("sending ", type, frameSize, streamId);
            }

            // Wait for other packets to be sent.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                await SendPacketAsync(data).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        internal async ValueTask ReceiveDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            for (int offset = 0; offset != buffer.Length;)
            {
                int received = await _socket.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
                offset += received;
                Received(received);
            }
        }

        internal void ReleaseStream(SlicStream stream)
        {
            Debug.Assert(!stream.IsControl);

            if (stream.IsIncoming)
            {
                if (stream.IsBidirectional)
                {
                    Interlocked.Decrement(ref _bidirectionalStreamCount);
                }
                else
                {
                    Interlocked.Decrement(ref _unidirectionalStreamCount);
                }
            }
            else
            {
                if (stream.IsBidirectional)
                {
                    _bidirectionalStreamSemaphore!.Release();
                }
                else
                {
                    _unidirectionalStreamSemaphore!.Release();
                }
            }
        }

        internal async ValueTask SendPacketAsync(IList<ArraySegment<byte>> buffer)
        {
            // Perform the write
            int sent = await _socket.SendAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            Debug.Assert(sent == buffer.GetByteCount());
            Sent(sent);
        }

        internal async ValueTask SendStreamFrameAsync(
            SlicStream stream,
            int packetSize,
            bool fin,
            IList<ArraySegment<byte>> buffer,
            CancellationToken cancel)
        {
            if (stream.IsStarted || stream.IsControl)
            {
                // Wait for queued packets to be sent. If this is canceled, the caller is responsible for
                // ensuring that the stream is released. If it's an incoming stream, the stream is released
                // by SlicStream.Destroy(). For outgoing streams, the stream is released once the peer sends
                // the StreamLast frame after receiving the stream reset frame.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            }
            else
            {
                // If the outgoing stream isn't started, we need to increase the semaphore count to
                // ensure we don't open more streams than the peer allows. The semaphore provides FIFO
                // guarantee to ensure that the sending of requests is serialized.
                Debug.Assert(!stream.IsIncoming);
                if (stream.IsBidirectional)
                {
                    await _bidirectionalStreamSemaphore!.EnterAsync(cancel).ConfigureAwait(false);
                }
                else
                {
                    await _unidirectionalStreamSemaphore!.EnterAsync(cancel).ConfigureAwait(false);
                }

                // Wait for queued packets to be sent.
                try
                {
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                }
                catch
                {
                    // The stream isn't started so we're responsible for releasing it. No stream reset will be
                    // sent to the peer for streams which are not started.
                    stream.Release();
                    throw;
                }
            }

            // Once we acquired the send semaphore, the sending is no longer cancellable. We can't interrupt a
            // send on the underlying socket and we want to make sure that once a stream is started, the peer
            // will always receive at least one stream frame.

            try
            {
                if (!stream.IsStarted)
                {
                    // Allocate a new ID according to the Quic numbering scheme.
                    if (stream.IsBidirectional)
                    {
                        stream.Id = _nextBidirectionalId;
                        _nextBidirectionalId += 4;
                    }
                    else
                    {
                        stream.Id = _nextUnidirectionalId;
                        _nextUnidirectionalId += 4;
                    }
                }

                // The incoming bidirectional stream is considered completed once no more data will be written on
                // the stream. It's important to release the stream here before the peer receives the last stream
                // frame to prevent a race where the peer could start a new stream before the stream count is
                // decreased by release. If the stream is already released, this indicates that the stream got
                // reset. In this case, we return since an empty stream last frame has been sent already.
                if (stream.IsIncoming && fin && !stream.Release())
                {
                    return;
                }

                // Compute how much space the size and stream ID require to figure out the start of the Slic header.
                int streamIdLength = OutputStream.GetSizeLength20(stream.Id);
                packetSize += streamIdLength;
                int sizeLength = OutputStream.GetSizeLength20(packetSize);

                SlicDefinitions.FrameType frameType =
                    fin ? SlicDefinitions.FrameType.StreamLast : SlicDefinitions.FrameType.Stream;

                // Write the Slic frame header (frameType - byte, frameSize - varint, streamId - varlong). Since
                // we might not need the full space reserved for the header, we modify the send buffer to ensure
                // the first element points at the start of the Slic header. We'll restore the send buffer once
                // the send is complete (it's important for the tracing code which might rely on the encoded
                // data).
                ArraySegment<byte> previous = buffer[0];
                ArraySegment<byte> headerData =
                    buffer[0].Slice(SlicDefinitions.FrameHeader.Length - sizeLength - streamIdLength - 1);
                headerData[0] = (byte)frameType;
                headerData.AsSpan(1, sizeLength).WriteFixedLengthSize20(packetSize);
                headerData.AsSpan(1 + sizeLength, streamIdLength).WriteFixedLengthSize20(stream.Id);
                buffer[0] = headerData;

                if (Endpoint.Communicator.TraceLevels.Transport > 2)
                {
                    TraceTransportFrame("sending ", frameType, packetSize, stream.Id);
                }

                try
                {
                    await SendPacketAsync(buffer).ConfigureAwait(false);
                }
                finally
                {
                    buffer[0] = previous; // Restore the original value of the send buffer.
                }
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        internal void TraceTransportFrame(string prefix, SlicDefinitions.FrameType type, int size, long? streamId)
        {
            string frameType = "Slic " + type switch
            {
                SlicDefinitions.FrameType.Initialize => "initialize",
                SlicDefinitions.FrameType.InitializeAck => "initialize acknowledgment",
                SlicDefinitions.FrameType.Version => "version",
                SlicDefinitions.FrameType.Ping => "ping",
                SlicDefinitions.FrameType.Pong => "pong",
                SlicDefinitions.FrameType.Stream => "stream",
                SlicDefinitions.FrameType.StreamLast => "last stream",
                SlicDefinitions.FrameType.StreamReset => "reset stream",
                SlicDefinitions.FrameType.StreamConsumed => "consumed stream",
                _ => "unknown",
            } + " frame";

            var s = new StringBuilder();
            s.Append(prefix);
            s.Append(frameType);

            s.Append("\nprotocol = ");
            s.Append(Endpoint.Protocol.GetName());

            s.Append("\nframe size = ");
            s.Append(size);

            if (streamId != null)
            {
                s.Append("\nstream ID = ");
                s.Append(streamId);
            }

            s.Append('\n');
            s.Append(Underlying.ToString());

            Endpoint.Communicator.Logger.Trace(TraceLevels.TransportCategory, s.ToString());
        }

        private void ReadParameters(InputStream istr)
        {
            TimeSpan? peerIdleTimeout = null;
            int dictionarySize = istr.ReadSize();
            for (int i = 0; i < dictionarySize; ++i)
            {
                (int key, ReadOnlyMemory<byte> value) = istr.ReadBinaryContextEntry();
                if (key == (int)ParameterKey.MaxBidirectionalStreams)
                {
                    _bidirectionalStreamSemaphore = new AsyncSemaphore((int)value.Span.ReadVarULong().Value);
                }
                else if (key == (int)ParameterKey.MaxUnidirectionalStreams)
                {
                    _unidirectionalStreamSemaphore = new AsyncSemaphore((int)value.Span.ReadVarULong().Value);
                }
                else if (key == (int)ParameterKey.IdleTimeout)
                {
                    // Use the smallest idle timeout.
                    peerIdleTimeout = TimeSpan.FromMilliseconds(value.Span.ReadVarULong().Value);
                    if (peerIdleTimeout < IdleTimeout)
                    {
                        _idleTimeout = peerIdleTimeout.Value;
                    }
                }
                else if (key == (int)ParameterKey.PacketMaxSize)
                {
                    PeerPacketMaxSize = (int)value.Span.ReadVarULong().Value;
                }
                else if (key == (int)ParameterKey.StreamBufferMaxSize)
                {
                    PeerStreamBufferMaxSize = (int)value.Span.ReadVarULong().Value;
                }
                else
                {
                    // Ignore unsupported parameters
                }
            }

            // Now, ensure required parameters are set.

            if (_bidirectionalStreamSemaphore == null)
            {
                throw new InvalidDataException("missing MaxBidirectionalStreams Slic connection parameter");
            }

            if (_unidirectionalStreamSemaphore == null)
            {
                throw new InvalidDataException("missing MaxUnidirectionalStreams Slic connection parameter");
            }

            if (IsIncoming && peerIdleTimeout == null)
            {
                // The client must send its idle timeout parameter. A server can however omit the idle timeout if its
                // configured idle timeout is larger than the client's idle timeout.
                throw new InvalidDataException("missing IdleTimeout Slic connection parameter");
            }

            if (PeerPacketMaxSize < 1024)
            {
                throw new InvalidDataException($"invalid PacketMaxSize={PeerPacketMaxSize} Slic connection parameter");
            }
        }

        private async ValueTask<(SlicDefinitions.FrameType, ArraySegment<byte>)> ReceiveFrameAsync(
            CancellationToken cancel)
        {
            (SlicDefinitions.FrameType type, int size, long? _) = await ReceiveHeaderAsync(cancel).ConfigureAwait(false);
            ArraySegment<byte> data;
            if (size > 0)
            {
                data = new byte[size];
                await ReceiveDataAsync(data, cancel).ConfigureAwait(false);
            }
            else
            {
                data = ArraySegment<byte>.Empty;
            }
            return (type, data);
        }

        private async ValueTask<(SlicDefinitions.FrameType, int, long?)> ReceiveHeaderAsync(CancellationToken cancel)
        {
            // Receive at most 2 bytes for the Slic header (the minimum size of a Slic header). The first byte
            // will be the frame type and the second is the first byte of the Slic frame size.
            ReadOnlyMemory<byte> buffer = await _socket.ReceiveAsync(2, cancel).ConfigureAwait(false);
            var type = (SlicDefinitions.FrameType)buffer.Span[0];
            int sizeLength = buffer.Span[1].ReadSizeLength20();
            int size;
            if (sizeLength > 1)
            {
                _socket.Rewind(1);
                buffer = await _socket.ReceiveAsync(sizeLength, cancel).ConfigureAwait(false);
                size = buffer.Span.ReadSize20().Size;
            }
            else
            {
                size = buffer.Span[1..2].ReadSize20().Size;
            }

            // Receive the stream ID if the frame includes a stream ID. We receive at most 8 or size bytes and rewind
            // the socket buffered position if we read too much data.
            (ulong? streamId, int streamIdLength) = (null, 0);
            if (type >= SlicDefinitions.FrameType.Stream && type <= SlicDefinitions.FrameType.StreamConsumed)
            {
                int receiveSize = Math.Min(size, 8);
                buffer = await _socket.ReceiveAsync(receiveSize, cancel).ConfigureAwait(false);
                (streamId, streamIdLength) = buffer.Span.ReadVarULong();
                _socket.Rewind(receiveSize - streamIdLength);
            }

            Received(1 + sizeLength + streamIdLength);

            if (Endpoint.Communicator.TraceLevels.Transport > 2)
            {
                TraceTransportFrame("receiving ", type, size, (long?)streamId);
            }

            // The size check doesn't include the stream ID length
            size -= streamIdLength;
            if (size > PeerPacketMaxSize)
            {
                throw new InvalidDataException("peer sent Slic packet larger than the configured packet maximum size");
            }
            return (type, size, (long?)streamId);
        }

        private async ValueTask WaitForReceivedStreamDataCompletionAsync(CancellationToken cancel)
        {
            // If the stream didn't fully read the stream data, finish reading it here before returning. The stream
            // might not have fully received the data if it was aborted or canceled.
            int size = await _receiveStreamCompletionTaskSource.ValueTask.WaitAsync(cancel).ConfigureAwait(false);
            if (size > 0)
            {
                var receiveBuffer = new ArraySegment<byte>(ArrayPool<byte>.Shared.Rent(size), 0, size);
                try
                {
                    await ReceiveDataAsync(receiveBuffer, cancel).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(receiveBuffer.Array!);
                }
            }
        }

        private void WriteParameters(OutputStream ostr)
        {
            // Client connections always send the idle timeout. On the server side however, if the received client's
            // idle timeout is smaller then the configured idle timeout, we can omit sending the server's idle timeout.
            bool writeIdleTimeout = !IsIncoming || Endpoint.Communicator.IdleTimeout < _idleTimeout;

            ostr.WriteSize(writeIdleTimeout ? 5 : 4);
            ostr.WriteBinaryContextEntry((int)ParameterKey.MaxBidirectionalStreams,
                                         (ulong)_maxBidirectionalStreams,
                                         OutputStream.IceWriterFromVarULong);
            ostr.WriteBinaryContextEntry((int)ParameterKey.MaxUnidirectionalStreams,
                                         (ulong)_maxUnidirectionalStreams,
                                         OutputStream.IceWriterFromVarULong);
            if (writeIdleTimeout)
            {
                ostr.WriteBinaryContextEntry((int)ParameterKey.IdleTimeout,
                                             (ulong)_idleTimeout.TotalMilliseconds,
                                             OutputStream.IceWriterFromVarULong);
            }

            ostr.WriteBinaryContextEntry((int)ParameterKey.PacketMaxSize,
                                         (ulong)Endpoint.Communicator.SlicPacketMaxSize,
                                         OutputStream.IceWriterFromVarULong);

            ostr.WriteBinaryContextEntry((int)ParameterKey.StreamBufferMaxSize,
                                         (ulong)Endpoint.Communicator.SlicStreamBufferMaxSize,
                                         OutputStream.IceWriterFromVarULong);
        }
    }
}
