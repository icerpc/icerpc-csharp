// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slic;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic connection implements a multi-stream transport on top of a single-stream transport such
    /// as TCP. It supports the same set of features as Quic.</summary>
    internal class SlicConnection : MultiStreamOverSingleStreamConnection
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
        private readonly int _bidirectionalMaxStreams;
        private readonly int _unidirectionalMaxStreams;
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private readonly ManualResetValueTaskCompletionSource<int> _receiveStreamCompletionTaskSource = new();
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private BufferedReceiveOverSingleStreamConnection? _bufferedConnection;
        private Memory<byte>? _streamConsumedBuffer;
        private int _unidirectionalStreamCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;

        public override async ValueTask<Stream> AcceptStreamAsync(CancellationToken cancel)
        {
            // Eventually wait for the stream data receive to complete if stream data is being received.
            await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);

            while (true)
            {
                (SlicDefinitions.FrameType type, int frameSize) =
                    await ReceiveHeaderAsync(cancel).ConfigureAwait(false);

                switch (type)
                {
                    case SlicDefinitions.FrameType.Close:
                    {
                        Logger.LogReceivingSlicFrame(type, frameSize);
                        throw new ConnectionClosedException();
                    }
                    case SlicDefinitions.FrameType.Ping:
                    {
                        Logger.LogReceivingSlicFrame(type, frameSize);
                        if (frameSize != 0)
                        {
                            throw new InvalidDataException("unexpected data for Slic Ping fame");
                        }
                        _ = PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Pong, cancel: CancellationToken.None);
                        PingReceived?.Invoke();
                        break;
                    }
                    case SlicDefinitions.FrameType.Pong:
                    {
                        Logger.LogReceivingSlicFrame(type, frameSize);
                        if (frameSize != 0)
                        {
                            throw new InvalidDataException("unexpected data for Slic Pong fame");
                        }
                        // TODO: setup and reset timer here for the pong frame response?
                        break;
                    }
                    case SlicDefinitions.FrameType.Stream:
                    case SlicDefinitions.FrameType.StreamLast:
                    {
                        (long streamId, int dataSize) =
                            await ReceiveStreamIdAsync(frameSize, cancel).ConfigureAwait(false);

                        if (dataSize == 0 && type == SlicDefinitions.FrameType.Stream)
                        {
                            throw new InvalidDataException("received empty stream frame");
                        }

                        using (IDisposable? scope = Logger.StartStreamScope(streamId))
                        {
                            Logger.LogReceivingSlicFrame(type, frameSize);
                        }

                        bool isIncoming = streamId % 2 == (IsIncoming ? 0 : 1);
                        bool isBidirectional = streamId % 4 < 2;
                        bool fin = type == SlicDefinitions.FrameType.StreamLast;

                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            // Notify the stream that data is available for read.
                            stream.ReceivedFrame(dataSize, fin);

                            // Wait for the stream to receive the data before reading a new Slic frame.
                            if (dataSize > 0)
                            {
                                await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);
                            }
                        }
                        else if (isIncoming && IsIncomingStreamUnknown(streamId, isBidirectional))
                        {
                            // Create a new stream if the incoming stream is unknown (the client could be sending
                            // frames for old canceled incoming streams, these are ignored).

                            if (dataSize == 0)
                            {
                                throw new InvalidDataException("received empty stream frame on new stream");
                            }

                            // Accept the new incoming stream and notify the stream that data is available.
                            try
                            {
                                stream = new SlicStream(this, streamId);
                            }
                            catch
                            {
                                // The connection is being closed, we make sure to receive the frame data. When the
                                // connection is being closed gracefully, the connection waits for the connection to
                                // receive the RST from the peer so it's important to receive and skip all the
                                // data until the RST is received.
                                await IgnoreDataAsync(dataSize, cancel).ConfigureAwait(false);
                                continue;
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
                            stream.ReceivedFrame(dataSize, fin);
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
                            if (dataSize > 0)
                            {
                                await IgnoreDataAsync(dataSize, cancel).ConfigureAwait(false);
                            }
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamReset:
                    {
                        (long streamId, int dataSize) =
                            await ReceiveStreamIdAsync(frameSize, cancel).ConfigureAwait(false);
                        if (streamId == 2 || streamId == 3)
                        {
                            throw new InvalidDataException("can't reset control streams");
                        }

                        using IDisposable? scope = Logger.StartStreamScope(streamId);
                        Logger.LogReceivingSlicFrame(type, frameSize);

                        ArraySegment<byte> data = new byte[dataSize];
                        await ReceiveDataAsync(data, cancel).ConfigureAwait(false);

                        var istr = new InputStream(data, SlicDefinitions.Encoding);
                        var streamReset = new StreamResetBody(istr);
                        var errorCode = (StreamErrorCode)streamReset.ApplicationProtocolErrorCode;
                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            stream.ReceivedReset(errorCode);
                        }
                        else
                        {
                            bool isIncoming = streamId % 2 == (IsIncoming ? 0 : 1);
                            bool isBidirectional = streamId % 4 < 2;
                            // Release the stream count for the destroyed stream if it's an outgoing stream. For
                            // incoming streams, the stream count is released on shutdown of the stream.
                            if (!isIncoming)
                            {
                                if (isBidirectional)
                                {
                                    _bidirectionalStreamSemaphore!.Release();
                                }
                                else
                                {
                                    _unidirectionalStreamSemaphore!.Release();
                                }
                            }
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamConsumed:
                    {
                        (long streamId, int dataSize) =
                            await ReceiveStreamIdAsync(frameSize, cancel).ConfigureAwait(false);
                        if (streamId == 2 || streamId == 3)
                        {
                            throw new InvalidDataException("control streams don't support flow control");
                        }
                        if (frameSize > 8)
                        {
                            throw new InvalidDataException("stream consumed frame too large");
                        }

                        using IDisposable? scope = Logger.StartStreamScope(streamId);
                        Logger.LogReceivingSlicFrame(type, frameSize);

                        _streamConsumedBuffer ??= new byte[8];

                        await ReceiveDataAsync(_streamConsumedBuffer.Value[0..dataSize], cancel).ConfigureAwait(false);

                        var istr = new InputStream(_streamConsumedBuffer.Value[0..dataSize], SlicDefinitions.Encoding);
                        var streamConsumed = new StreamConsumedBody(istr);
                        if (TryGetStream(streamId, out SlicStream? stream))
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

        public override async ValueTask CloseAsync(ConnectionErrorCode errorCode, CancellationToken cancel)
        {
            await Underlying.CloseAsync((long)errorCode, cancel).ConfigureAwait(false);

            await PrepareAndSendFrameAsync(
                SlicDefinitions.FrameType.Close,
                ostr =>
                {
                    checked
                    {
                        new CloseBody((ulong)errorCode).IceWrite(ostr);
                    }
                },
                frameSize => Logger.LogSendingSlicFrame(SlicDefinitions.FrameType.Close, frameSize),
                cancel: cancel).ConfigureAwait(false);
        }

        public override Stream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new SlicStream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public override async ValueTask InitializeAsync(CancellationToken cancel)
        {
            // Create a buffered receive single stream on top of the underlying connection.
            _bufferedConnection = new BufferedReceiveOverSingleStreamConnection(Underlying);

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
                        frameSize => Logger.LogSendingSlicVersionFrame(frameSize, versionBody),
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
                Logger.LogReceivingSlicInitializeFrame(data.Count, version, initializeBody, parameters);

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
                    frameSize => Logger.LogSendingSlicInitializeAckFrame(frameSize, parameters),
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
                    frameSize => Logger.LogSendingSlicInitializeFrame(frameSize, version, initializeBody, parameters),
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
                    Logger.LogReceivingSlicVersionFrame(data.Count, versionBody);

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
                    Logger.LogReceivingSlicInitializeAckFrame(data.Count, parameters);
                    SetParameters(parameters);
                }
            }
        }

        public override Task PingAsync(CancellationToken cancel) =>
            // TODO: shall we set a timer for expecting the Pong frame? or should we return only once
            // the pong from is received? which timeout to use for expecting the pong frame?
            PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Ping, cancel: cancel);

        internal SlicConnection(
            Endpoint endpoint,
            SingleStreamConnection singleStreamConnection,
            ConnectionOptions options)
            : base(endpoint, singleStreamConnection, options)
        {
            _idleTimeout = options.IdleTimeout;
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
        }

        internal override void AbortStreams(StreamErrorCode errorCode)
        {
            base.AbortStreams(errorCode);

            // Unblock requests waiting on the semaphores.
            var exception = new ConnectionClosedException();
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);
        }

        internal void FinishedReceivedStreamData(int remainingSize)
        {
            Debug.Assert(!_receiveStreamCompletionTaskSource.IsCompleted);
            _receiveStreamCompletionTaskSource.SetResult(remainingSize);
        }

        internal async Task PrepareAndSendFrameAsync(
            SlicDefinitions.FrameType type,
            Action<OutputStream>? writer = null,
            Action<int>? logAction = null,
            SlicStream? stream = null,
            CancellationToken cancel = default)
        {
            Debug.Assert(stream == null ?
                type < SlicDefinitions.FrameType.Stream || type > SlicDefinitions.FrameType.StreamConsumed :
                type >= SlicDefinitions.FrameType.Stream || type <= SlicDefinitions.FrameType.StreamConsumed);

            var data = new List<Memory<byte>>();
            var ostr = new OutputStream(SlicDefinitions.Encoding, data);
            ostr.WriteByte((byte)type);
            OutputStream.Position sizePos = ostr.StartFixedLengthSize(4);
            if (stream != null)
            {
                ostr.WriteVarULong((ulong)stream.Id);
            }
            writer?.Invoke(ostr);
            int frameSize = ostr.Tail.Offset - sizePos.Offset - 4;
            ostr.EndFixedLengthSize(sizePos, 4);
            ostr.Finish();

            // Wait for other packets to be sent.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                using IDisposable? scope = stream?.StartScope();
                if (logAction != null)
                {
                    logAction?.Invoke(frameSize);
                }
                else
                {
                    Logger.LogSendingSlicFrame(type, frameSize);
                }
                await SendPacketAsync(data.ToReadOnlyMemory()).ConfigureAwait(false);
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
                offset += await _bufferedConnection!.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
            }
            Received(buffer);
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

        internal async ValueTask SendPacketAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            for (int i = 0; i < buffers.Length; ++i)
            {
                // A Slic packet must always be sent entirely even if the sending of the stream data is canceled.
                await _bufferedConnection!.SendAsync(buffers.Span[i], CancellationToken.None).ConfigureAwait(false);
            }
            Sent(buffers);
        }

        internal async ValueTask SendStreamFrameAsync(
            SlicStream stream,
            int packetSize,
            bool endStream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
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

            if (IsIncoming && endStream)
            {
                // Release the stream count if it's the last frame. It's important to release the count before to
                // send the last frame to prevent a race condition with the client.
                if (!stream.ReleaseStreamCount())
                {
                    // If the stream is already released, it's because it was aborted.
                    Debug.Assert(stream.IsAborted);
                }
            }

            // If the stream is aborted, stop sending stream frames.
            if (stream.AbortException is Exception exception)
            {
                _sendSemaphore.Release();
                throw exception;
            }

            // Once we acquired the send semaphore, the sending of the packet is no longer cancellable. We can't
            // interrupt a send on the underlying connection and we want to make sure that once a stream is started,
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
                        endStream ? SlicDefinitions.FrameType.StreamLast : SlicDefinitions.FrameType.Stream;

                    // Write the Slic frame header (frameType - byte, frameSize - varint, streamId - varlong). Since
                    // we might not need the full space reserved for the header, we modify the send buffer to ensure
                    // the first element points at the start of the Slic header. We'll restore the send buffer once
                    // the send is complete (it's important for the tracing code which might rely on the encoded
                    // data).
                    ReadOnlyMemory<byte> previous = buffers.Span[0];
                    Memory<byte> headerData = MemoryMarshal.AsMemory(
                        buffers.Span[0].Slice(SlicDefinitions.FrameHeader.Length - sizeLength - streamIdLength - 1));
                    headerData.Span[0] = (byte)frameType;
                    headerData.Span.Slice(1, sizeLength).WriteFixedLengthSize20(packetSize);
                    headerData.Span.Slice(1 + sizeLength, streamIdLength).WriteFixedLengthSize20(stream.Id);

                    // Update the first buffer entry
                    MemoryMarshal.AsMemory(buffers).Span[0] = headerData;

                    using IDisposable? scope = stream.StartScope();
                    Logger.LogSendingSlicFrame(frameType, packetSize);
                    try
                    {
                        await SendPacketAsync(buffers).ConfigureAwait(false);
                    }
                    finally
                    {
                        // Restore the original value of the send buffer.
                        MemoryMarshal.AsMemory(buffers).Span[0] = previous;
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

        private async ValueTask IgnoreDataAsync(int size, CancellationToken cancel)
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
            (SlicDefinitions.FrameType type, int size) = await ReceiveHeaderAsync(cancel).ConfigureAwait(false);
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

        private async ValueTask<(SlicDefinitions.FrameType, int)> ReceiveHeaderAsync(CancellationToken cancel)
        {
            // Receive at most 2 bytes for the Slic header (the minimum size of a Slic header). The first byte
            // will be the frame type and the second is the first byte of the Slic frame size.
            ReadOnlyMemory<byte> buffer = await _bufferedConnection!.ReceiveAsync(2, cancel).ConfigureAwait(false);
            var type = (SlicDefinitions.FrameType)buffer.Span[0];
            int sizeLength = buffer.Span[1].ReadSizeLength20();
            int size;
            if (sizeLength > 1)
            {
                Received(buffer.Slice(0, 1));
                _bufferedConnection!.Rewind(1);
                buffer = await _bufferedConnection!.ReceiveAsync(sizeLength, cancel).ConfigureAwait(false);
                size = buffer.Span.ReadSize20().Size;
                Received(buffer.Slice(0, sizeLength));
            }
            else
            {
                size = buffer.Span[1..2].ReadSize20().Size;
                Received(buffer.Slice(0, 2));
            }

            if (size - 8 > PeerPacketMaxSize)
            {
                throw new InvalidDataException("peer sent Slic packet larger than the configured packet maximum size");
            }

            return (type, size);
        }

        private async ValueTask<(long, int)> ReceiveStreamIdAsync(int frameSize, CancellationToken cancel)
        {
            // We receive at most 8 or maxSize bytes and rewind the connection buffered position if we read
            // too much data.
            int size = Math.Min(frameSize, 8);
            ReadOnlyMemory<byte> buffer = await _bufferedConnection!.ReceiveAsync(size, cancel).ConfigureAwait(false);
            (ulong streamId, int streamIdLength) = buffer.Span.ReadVarULong();
            _bufferedConnection!.Rewind(size - streamIdLength);
            Received(buffer[0..streamIdLength]);

            // Return the stream ID and the size of the data remaining to read for the frame.
            return ((long)streamId, frameSize - streamIdLength);
        }

        private async ValueTask WaitForReceivedStreamDataCompletionAsync(CancellationToken cancel)
        {
            // If the stream didn't fully read the stream data, finish reading it here before returning. The stream
            // might not have fully received the data if it was aborted or canceled.
            int size = await _receiveStreamCompletionTaskSource.ValueTask.IceWaitAsync(cancel).ConfigureAwait(false);
            if (size > 0)
            {
                await IgnoreDataAsync(size, cancel).ConfigureAwait(false);
            }
        }
    }
}
