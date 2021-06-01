// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slic;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
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
        internal int PacketMaxSize { get; }
        internal int PeerPacketMaxSize { get; private set; }
        internal int PeerStreamBufferMaxSize { get; private set; }
        internal int StreamBufferMaxSize { get; }

        private int _bidirectionalStreamCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private TimeSpan _idleTimeout;
        private long _lastBidirectionalId;
        private long _lastUnidirectionalId;
        private readonly int _bidirectionalMaxStreams;
        private readonly int _unidirectionalMaxStreams;
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private readonly ManualResetValueTaskCompletionSource<int> _receiveStreamCompletionTaskSource = new();
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private BufferedReceiveOverSingleStreamSocket? _socket;
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
                        Logger.LogReceivedSlicFrame(type, size);

                        if (size != 0)
                        {
                            throw new InvalidDataException("unexpected data for Slic Ping fame");
                        }
                        _ = PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Pong, cancel: CancellationToken.None);
                        PingReceived?.Invoke();
                        break;
                    }
                    case SlicDefinitions.FrameType.Pong:
                    {
                        Logger.LogReceivedSlicFrame(type, size);

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
                                // The socket is being closed, we make sure to receive the frame data. When the
                                // connection is being closed gracefully, the connection waits for the socket to
                                // receive the RST from the peer so it's important to receive and skip all the
                                // data until the RST is received.
                                _receiveStreamCompletionTaskSource.SetResult(size);
                                throw;
                            }

                            if (stream.IsControl)
                            {
                                // We don't acquire stream count for the control stream.
                            }
                            else if (isBidirectional)
                            {
                                if (_bidirectionalStreamCount == _bidirectionalMaxStreams)
                                {
                                    throw new InvalidDataException(
                                        $"maximum bidirectional stream count {_bidirectionalMaxStreams} reached");
                                }
                                Interlocked.Increment(ref _bidirectionalStreamCount);
                            }
                            else
                            {
                                if (_unidirectionalStreamCount == _unidirectionalMaxStreams)
                                {
                                    throw new InvalidDataException(
                                        $"maximum unidirectional stream count {_unidirectionalMaxStreams} reached");
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

                            using IDisposable? scope = Logger.StartStreamScope(streamId.Value);
                            Logger.LogReceivedSlicFrame(
                                fin ? SlicDefinitions.FrameType.StreamLast : SlicDefinitions.FrameType.Stream, size);
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

                        using IDisposable? scope = Logger.StartStreamScope(streamId.Value);
                        Logger.LogReceivedSlicResetFrame(
                            size,
                            (StreamResetErrorCode)streamReset.ApplicationProtocolErrorCode);
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamConsumed:
                    {
                        Debug.Assert(streamId != null);

                        using IDisposable? scope = Logger.StartStreamScope(streamId.Value);
                        Logger.LogReceivedSlicFrame(type, size);

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
                        throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                    }
                }
            }
        }

        public override ValueTask CloseAsync(Exception exception, CancellationToken cancel) =>
            (_socket ?? Underlying).CloseAsync(exception, cancel);

        public override SocketStream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new SlicStream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public override async ValueTask InitializeAsync(CancellationToken cancel)
        {
            // Create a buffered receive single stream socket on top of the underlying socket.
            _socket = new BufferedReceiveOverSingleStreamSocket(Underlying);

            if (IsIncoming)
            {
                (SlicDefinitions.FrameType type, ArraySegment<byte> data) =
                    await ReceiveFrameAsync(cancel).ConfigureAwait(false);

                if (type != SlicDefinitions.FrameType.Initialize)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                }

                // Check that the Slic version is supported (we only support version 1 for now)
                var istr = new InputStream(data, SlicDefinitions.Encoding);
                uint version = istr.ReadVarUInt();
                if (version != 1)
                {
                    Logger.LogSlicReceivedUnsupportedInitializeFrame(data.Count, version);

                    // If unsupported Slic version, we stop reading there and reply with a Version frame to provide
                    // the client the supported Slic versions.
                    var versionBody = new VersionBody(new uint[] { 1 });
                    await PrepareAndSendFrameAsync(
                        SlicDefinitions.FrameType.Version,
                        ostr => versionBody.IceWrite(ostr),
                        frameSize => Logger.LogSentSlicVersionFrame(frameSize, versionBody),
                        cancel: cancel).ConfigureAwait(false);

                    (type, data) = await ReceiveFrameAsync(cancel).ConfigureAwait(false);
                    if (type != SlicDefinitions.FrameType.Initialize)
                    {
                        throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                    }

                    istr = new InputStream(data, SlicDefinitions.Encoding);
                    version = istr.ReadVarUInt();
                    if (version != 1)
                    {
                        throw new InvalidDataException($"unsupported Slic version '{version}'");
                    }
                }

                // Read initialize frame
                var initializeBody = new InitializeHeaderBody(istr);
                Dictionary<ParameterKey, ulong> parameters = ReadParameters(istr);
                Logger.LogReceivedSlicInitializeFrame(data.Count, version, initializeBody, parameters);

                // Check the application protocol and set the parameters.
                try
                {
                    if (ProtocolExtensions.Parse(initializeBody.ApplicationProtocolName) != Protocol.Ice2)
                    {
                        throw new NotSupportedException(
                            $"application protocol '{initializeBody.ApplicationProtocolName}' is not supported");
                    }
                }
                catch (FormatException ex)
                {
                    throw new NotSupportedException(
                        $"unknown application protocol '{initializeBody.ApplicationProtocolName}'", ex);
                }
                SetParameters(parameters);

                // Send back an InitializeAck frame.
                parameters = GetParameters();
                await PrepareAndSendFrameAsync(
                    SlicDefinitions.FrameType.InitializeAck,
                    ostr => WriteParameters(ostr, parameters),
                    frameSize => Logger.LogSentSlicInitializeAckFrame(frameSize, parameters),
                    cancel: cancel).ConfigureAwait(false);
            }
            else
            {
                // Send the Initialize frame.
                const uint version = 1;
                var initializeBody = new InitializeHeaderBody(Protocol.Ice2.GetName());
                Dictionary<ParameterKey, ulong> parameters = GetParameters();
                await PrepareAndSendFrameAsync(
                    SlicDefinitions.FrameType.Initialize,
                    ostr =>
                    {
                        ostr.WriteVarUInt(version);
                        initializeBody.IceWrite(ostr);
                        WriteParameters(ostr, parameters);
                    },
                    frameSize => Logger.LogSentSlicInitializeFrame(frameSize, version, initializeBody, parameters),
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
                    Logger.LogReceivedSlicVersionFrame(data.Count, versionBody);

                    throw new InvalidDataException(
                        $"unsupported Slic version, server supports Slic '{string.Join(", ", versionBody.Versions)}'");
                }
                else if (type != SlicDefinitions.FrameType.InitializeAck)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                }
                else
                {
                    // Read and set parameters.
                    parameters = ReadParameters(istr);
                    Logger.LogReceivedSlicInitializeAckFrame(data.Count, parameters);
                    SetParameters(parameters);
                }
            }
        }

        public override Task PingAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            // TODO: shall we set a timer for expecting the Pong frame? or should we return only once
            // the pong from is received? which timeout to use for expecting the pong frame?
            return PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Ping, cancel: cancel);
        }

        internal SlicSocket(Endpoint endpoint, SingleStreamSocket socket, ConnectionOptions options)
            : base(endpoint, socket, options)
        {
            _idleTimeout = options.IdleTimeout;
            _receiveStreamCompletionTaskSource.RunContinuationAsynchronously = true;
            _receiveStreamCompletionTaskSource.SetResult(0);

            TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;
            PacketMaxSize = tcpOptions.SlicPacketMaxSize;
            StreamBufferMaxSize = tcpOptions.SlicStreamBufferMaxSize;

            // Initially set the peer packet max size to the local max size to ensure we can receive the first
            // initialize frame.
            PeerPacketMaxSize = PacketMaxSize;
            PeerStreamBufferMaxSize = StreamBufferMaxSize;

            // Configure the maximum stream counts to ensure the peer won't open more than one stream.
            _bidirectionalMaxStreams = options.BidirectionalStreamMaxCount;
            _unidirectionalMaxStreams = options.UnidirectionalStreamMaxCount;

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
            var ex = new ConnectionClosedException();
            _bidirectionalStreamSemaphore?.Complete(ex);
            _unidirectionalStreamSemaphore?.Complete(ex);

            return streamIds;
        }

        internal void FinishedReceivedStreamData(int frameSize, bool fin, int remainingSize)
        {
            Logger.LogReceivedSlicFrame(
                fin ? SlicDefinitions.FrameType.StreamLast : SlicDefinitions.FrameType.Stream,
                frameSize);
            _receiveStreamCompletionTaskSource.SetResult(remainingSize);
        }

        internal async Task PrepareAndSendFrameAsync(
            SlicDefinitions.FrameType type,
            Action<OutputStream>? writer = null,
            Action<int>? logAction = null,
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

            // Wait for other packets to be sent.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                await SendPacketAsync(data).ConfigureAwait(false);

                if (logAction != null)
                {
                    logAction?.Invoke(frameSize);
                }
                else
                {
                    Logger.LogSentSlicFrame(type, frameSize);
                }
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
                int received = await _socket!.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
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
            int sent = await _socket!.SendAsync(buffer, CancellationToken.None).ConfigureAwait(false);
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
                    stream.ReleaseStreamCount();
                    throw;
                }
            }

            bool started = stream.IsStarted;
            if (!started)
            {
                try
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
                catch
                {
                    _sendSemaphore.Release();
                    throw;
                }
            }

            // If the stream wasn't started, we start the scope here because the caller can't start it until
            // the stream is started.
            using IDisposable? streamScope = started ? null : stream.StartScope();

            // The incoming bidirectional stream is considered completed once no more data will be written on
            // the stream. It's important to release the stream here before the peer receives the last stream
            // frame to prevent a race where the peer could start a new stream before the stream count is
            // decreased by release. If the stream is already released, this indicates that the stream got
            // reset. In this case, we return since an empty stream last frame has been sent already.
            if (stream.IsIncoming && fin && !stream.ReleaseStreamCount())
            {
                return;
            }

            // Once we acquired the send semaphore, the sending of the packet is no longer cancellable. We can't
            // interrupt a send on the underlying socket and we want to make sure that once a stream is started,
            // the peer will always receive at least one stream frame.
            await PerformSendPacketAsync().IceWaitAsync(cancel).ConfigureAwait(false);

            async ValueTask PerformSendPacketAsync()
            {
                try
                {
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

                    Logger.LogSentSlicFrame(frameType, packetSize);

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
        }

        private static void WriteParameters(OutputStream ostr, Dictionary<ParameterKey, ulong> parameters)
        {
            ostr.WriteSize(parameters.Count);
            foreach ((ParameterKey key, ulong value) in parameters)
            {
                ostr.WriteField((int)key, value, OutputStream.IceWriterFromVarULong);
            }
        }

        private static Dictionary<ParameterKey, ulong> ReadParameters(InputStream istr)
        {
            int dictionarySize = istr.ReadSize();
            var parameters = new Dictionary<ParameterKey, ulong>();
            for (int i = 0; i < dictionarySize; ++i)
            {
                (int key, ReadOnlyMemory<byte> value) = istr.ReadField();
                parameters.Add((ParameterKey)key, value.Span.ReadVarULong().Value);
            }
            return parameters;
        }

        private Dictionary<ParameterKey, ulong> GetParameters() =>
            new()
            {
                { ParameterKey.MaxBidirectionalStreams, (ulong)_bidirectionalMaxStreams },
                { ParameterKey.MaxUnidirectionalStreams, (ulong)_unidirectionalMaxStreams },
                { ParameterKey.IdleTimeout, (ulong)_idleTimeout.TotalMilliseconds },
                { ParameterKey.PacketMaxSize, (ulong)PacketMaxSize },
                { ParameterKey.StreamBufferMaxSize, (ulong)StreamBufferMaxSize }
            };

        private void SetParameters(Dictionary<ParameterKey, ulong> parameters)
        {
            TimeSpan? peerIdleTimeout = null;
            foreach ((ParameterKey key, ulong value) in parameters)
            {
                if (key == ParameterKey.MaxBidirectionalStreams)
                {
                    _bidirectionalStreamSemaphore = new AsyncSemaphore((int)value);
                }
                else if (key == ParameterKey.MaxUnidirectionalStreams)
                {
                    _unidirectionalStreamSemaphore = new AsyncSemaphore((int)value);
                }
                else if (key == ParameterKey.IdleTimeout)
                {
                    // Use the smallest idle timeout.
                    peerIdleTimeout = TimeSpan.FromMilliseconds(value);
                    if (peerIdleTimeout < IdleTimeout)
                    {
                        _idleTimeout = peerIdleTimeout.Value;
                    }
                }
                else if (key == ParameterKey.PacketMaxSize)
                {
                    PeerPacketMaxSize = (int)value;
                }
                else if (key == ParameterKey.StreamBufferMaxSize)
                {
                    PeerStreamBufferMaxSize = (int)value;
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
            ReadOnlyMemory<byte> buffer = await _socket!.ReceiveAsync(2, cancel).ConfigureAwait(false);
            var type = (SlicDefinitions.FrameType)buffer.Span[0];
            int sizeLength = buffer.Span[1].ReadSizeLength20();
            int size;
            if (sizeLength > 1)
            {
                _socket!.Rewind(1);
                buffer = await _socket!.ReceiveAsync(sizeLength, cancel).ConfigureAwait(false);
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
                buffer = await _socket!.ReceiveAsync(receiveSize, cancel).ConfigureAwait(false);
                (streamId, streamIdLength) = buffer.Span.ReadVarULong();
                _socket!.Rewind(receiveSize - streamIdLength);
            }

            Received(1 + sizeLength + streamIdLength);

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
            int size = await _receiveStreamCompletionTaskSource.ValueTask.IceWaitAsync(cancel).ConfigureAwait(false);
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
    }
}
