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
    /// <summary>The Slic connection implements a multi-stream connection on top of a network socket such as TCP. It
    /// supports the same set of features as Quic.</summary>
    internal class SlicConnection : NetworkSocketConnection
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
        private BufferedReceiveOverNetworkSocket? _bufferedConnection;
        private Memory<byte>? _streamConsumedBuffer;
        private int _unidirectionalStreamCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;

        public override async ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel)
        {
            // Eventually wait for the stream data receive to complete if stream data is being received.
            await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);

            while (true)
            {
                (SlicDefinitions.FrameType type, int frameSize) =
                    await ReceiveHeaderAsync(cancel).ConfigureAwait(false);

                switch (type)
                {
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
                        bool fin = type == SlicDefinitions.FrameType.StreamLast;
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

                        bool isIncoming = streamId % 2 == (IsServer ? 0 : 1);
                        bool isBidirectional = streamId % 4 < 2;

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
                        else if (!isBidirectional && fin && streamId != 2 && streamId != 3)
                        {
                            // Release the stream count for the unidirectional stream.
                            _unidirectionalStreamSemaphore!.Release();
                        }
                        else
                        {
                            throw new InvalidDataException("received stream frame for unknown stream");
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamConsumed:
                    {
                        (long streamId, int dataSize) =
                            await ReceiveStreamIdAsync(frameSize, cancel).ConfigureAwait(false);
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream consumed frame too large");
                        }

                        using IDisposable? scope = Logger.StartStreamScope(streamId);
                        Logger.LogReceivingSlicFrame(type, frameSize);

                        _streamConsumedBuffer ??= new byte[8];

                        await ReceiveDataAsync(_streamConsumedBuffer.Value[0..dataSize], cancel).ConfigureAwait(false);

                        var iceDecoder = new IceDecoder(
                            _streamConsumedBuffer.Value[0..dataSize],
                            SlicDefinitions.Encoding);
                        var streamConsumed = new StreamConsumedBody(iceDecoder);
                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            stream.ReceivedConsumed((int)streamConsumed.Size);
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamReset:
                    {
                        (long streamId, int dataSize) =
                            await ReceiveStreamIdAsync(frameSize, cancel).ConfigureAwait(false);
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream reset frame too large");
                        }

                        using IDisposable? scope = Logger.StartStreamScope(streamId);

                        Memory<byte> data = new byte[dataSize];
                        await ReceiveDataAsync(data, cancel).ConfigureAwait(false);

                        var iceDecoder = new IceDecoder(data, SlicDefinitions.Encoding);
                        var streamReset = new StreamResetBody(iceDecoder);
                        var errorCode = (RpcStreamError)streamReset.ApplicationProtocolErrorCode;

                        Logger.LogReceivedSlicResetFrame(frameSize, errorCode);

                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            stream.ReceivedReset(errorCode);
                        }
                        break;
                    }
                    case SlicDefinitions.FrameType.StreamStopSending:
                    {
                        (long streamId, int dataSize) =
                            await ReceiveStreamIdAsync(frameSize, cancel).ConfigureAwait(false);
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream reset frame too large");
                        }

                        using IDisposable? scope = Logger.StartStreamScope(streamId);

                        Memory<byte> data = new byte[dataSize];
                        await ReceiveDataAsync(data, cancel).ConfigureAwait(false);

                        var iceDecoder = new IceDecoder(data, SlicDefinitions.Encoding);
                        var streamReset = new StreamResetBody(iceDecoder);
                        var errorCode = (RpcStreamError)streamReset.ApplicationProtocolErrorCode;

                        Logger.LogReceivedSlicStopSendingFrame(frameSize, errorCode);

                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            stream.TrySetWriteCompleted();
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

        public override RpcStream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new SlicStream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public override async ValueTask InitializeAsync(CancellationToken cancel)
        {
            // Create a buffered receive single stream on top of the underlying connection.
            _bufferedConnection = new BufferedReceiveOverNetworkSocket(NetworkSocket);

            if (IsServer)
            {
                (SlicDefinitions.FrameType type, ReadOnlyMemory<byte> data) =
                    await ReceiveFrameAsync(cancel).ConfigureAwait(false);

                if (type != SlicDefinitions.FrameType.Initialize)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                }

                // Check that the Slic version is supported (we only support version 1 for now)
                var iceDecoder = new IceDecoder(data, SlicDefinitions.Encoding);
                uint version = iceDecoder.DecodeVarUInt();
                if (version != 1)
                {
                    Logger.LogSlicReceivedUnsupportedInitializeFrame(data.Length, version);

                    // If unsupported Slic version, we stop reading there and reply with a Version frame to provide
                    // the client the supported Slic versions.
                    var versionBody = new VersionBody(new uint[] { 1 });
                    await PrepareAndSendFrameAsync(
                        SlicDefinitions.FrameType.Version,
                        iceEncoder => versionBody.IceWrite(iceEncoder),
                        frameSize => Logger.LogSendingSlicVersionFrame(frameSize, versionBody),
                        cancel: cancel).ConfigureAwait(false);

                    (type, data) = await ReceiveFrameAsync(cancel).ConfigureAwait(false);
                    if (type != SlicDefinitions.FrameType.Initialize)
                    {
                        throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                    }

                    iceDecoder = new IceDecoder(data, SlicDefinitions.Encoding);
                    version = iceDecoder.DecodeVarUInt();
                    if (version != 1)
                    {
                        throw new InvalidDataException($"unsupported Slic version '{version}'");
                    }
                }

                // Read initialize frame
                var initializeBody = new InitializeHeaderBody(iceDecoder);
                Dictionary<ParameterKey, ulong> parameters = ReadParameters(iceDecoder);
                Logger.LogReceivingSlicInitializeFrame(data.Length, version, initializeBody, parameters);

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
                    iceEncoder => WriteParameters(iceEncoder, parameters),
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
                    iceEncoder =>
                    {
                        iceEncoder.WriteVarUInt(version);
                        initializeBody.IceWrite(iceEncoder);
                        WriteParameters(iceEncoder, parameters);
                    },
                    frameSize => Logger.LogSendingSlicInitializeFrame(frameSize, version, initializeBody, parameters),
                    cancel: cancel).ConfigureAwait(false);

                // Read the InitializeAck or Version frame from the server
                (SlicDefinitions.FrameType type, ReadOnlyMemory<byte> data) =
                    await ReceiveFrameAsync(cancel).ConfigureAwait(false);

                var iceDecoder = new IceDecoder(data, SlicDefinitions.Encoding);

                // If we receive a Version frame, there isn't much we can do as we only support V1 so we throw
                // with an appropriate message to abort the connection.
                if (type == SlicDefinitions.FrameType.Version)
                {
                    // Read the version sequence provided by the server.
                    var versionBody = new VersionBody(iceDecoder);
                    Logger.LogReceivingSlicVersionFrame(data.Length, versionBody);

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
                    parameters = ReadParameters(iceDecoder);
                    Logger.LogReceivingSlicInitializeAckFrame(data.Length, parameters);
                    SetParameters(parameters);
                }
            }
        }

        public override Task PingAsync(CancellationToken cancel) =>
            // TODO: shall we set a timer for expecting the Pong frame? or should we return only once
            // the pong from is received? which timeout to use for expecting the pong frame?
            PrepareAndSendFrameAsync(SlicDefinitions.FrameType.Ping, cancel: cancel);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _bufferedConnection?.Dispose();

                // Unblock requests waiting on the semaphores.
                var exception = new ConnectionClosedException();
                _bidirectionalStreamSemaphore?.Complete(exception);
                _unidirectionalStreamSemaphore?.Complete(exception);
            }
        }

        internal SlicConnection(NetworkSocket networkSocket, Endpoint endpoint, ConnectionOptions options)
            : base(networkSocket, endpoint, options)
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
            if (IsServer)
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

        internal void FinishedReceivedStreamData(int remainingSize)
        {
            Debug.Assert(!_receiveStreamCompletionTaskSource.IsCompleted);
            _receiveStreamCompletionTaskSource.SetResult(remainingSize);
        }

        internal async Task PrepareAndSendFrameAsync(
            SlicDefinitions.FrameType type,
            Action<IceEncoder>? encodeAction = null,
            Action<int>? logAction = null,
            SlicStream? stream = null,
            CancellationToken cancel = default)
        {
            Debug.Assert(stream == null ?
                type < SlicDefinitions.FrameType.Stream || type > SlicDefinitions.FrameType.StreamConsumed :
                type >= SlicDefinitions.FrameType.Stream || type <= SlicDefinitions.FrameType.StreamConsumed);

            var iceEncoder = new IceEncoder(SlicDefinitions.Encoding);
            iceEncoder.WriteByte((byte)type);
            IceEncoder.Position sizePos = iceEncoder.StartFixedLengthSize(4);
            if (stream != null)
            {
                iceEncoder.WriteVarULong((ulong)stream.Id);
            }
            encodeAction?.Invoke(iceEncoder);
            int frameSize = iceEncoder.Tail.Offset - sizePos.Offset - 4;
            iceEncoder.EndFixedLengthSize(sizePos, 4);
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = iceEncoder.Finish();

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
                await SendPacketAsync(buffers).ConfigureAwait(false);
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
            if (!stream.IsControl)
            {
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
                else if (stream.IsBidirectional)
                {
                    _bidirectionalStreamSemaphore!.Release();
                }
                else
                {
                    // Don't release the semaphore for unidirectional streams. The semaphore will be released
                    // by AcceptStreamAsync when the peer sends a StreamLast frame.
                }
            }
        }

        private async ValueTask SendPacketAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            // Perform the write

            // A Slic packet must always be sent entirely even if the sending of the stream data is canceled.
            await _bufferedConnection!.SendAsync(buffers, CancellationToken.None).ConfigureAwait(false);
            Sent(buffers);
        }

        internal async ValueTask SendStreamFrameAsync(
            SlicStream stream,
            int packetSize,
            bool endStream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            AsyncSemaphore streamSemaphore = stream.IsBidirectional ?
                _bidirectionalStreamSemaphore! :
                _unidirectionalStreamSemaphore!;

            if (!stream.IsStarted && !stream.IsControl)
            {
                // If the outgoing stream isn't started, we need to acquire the stream semaphore to ensure we
                // don't open more streams than the peer allows.
                await streamSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            }

            try
            {
                // Wait for queued packets to be sent.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

                // If the stream is aborted, stop sending stream frames.
                if (stream.WriteCompleted)
                {
                    _sendSemaphore.Release();
                    throw new RpcStreamAbortedException(RpcStreamError.StreamAborted);
                }

                // Allocate stream ID if the stream isn't started. Thread-safety is provided by the send semaphore.
                if (!stream.IsStarted)
                {
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
            }
            catch
            {
                if (!stream.IsStarted && !stream.IsControl)
                {
                    streamSemaphore.Release();
                }
                throw;
            }

            if (endStream)
            {
                // At this point writes are considered completed on the stream. It's important to call this before
                // sending the last packet to avoid a race condition where the peer could start a new stream before
                // the Slic connection stream count is decreased.
                stream.TrySetWriteCompleted();
            }

            // Once we acquired the send semaphore, the sending of the packet is no longer cancellable. We can't
            // interrupt a send on the underlying connection and we want to make sure that once a stream is started,
            // the peer will always receive at least one stream frame.
            ValueTask sendPacketTask = PerformSendPacketAsync();
            if (sendPacketTask.IsCompleted)
            {
                await sendPacketTask.ConfigureAwait(false);
            }
            else
            {
                await sendPacketTask.AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }

            async ValueTask PerformSendPacketAsync()
            {
                try
                {
                    // Compute how much space the size and stream ID require to figure out the start of the Slic header.
                    int streamIdLength = IceEncoder.GetSizeLength20(stream.Id);
                    packetSize += streamIdLength;
                    int sizeLength = IceEncoder.GetSizeLength20(packetSize);

                    SlicDefinitions.FrameType frameType =
                        endStream ? SlicDefinitions.FrameType.StreamLast : SlicDefinitions.FrameType.Stream;

                    // Write the Slic frame header (frameType - byte, frameSize - varint, streamId - varlong). Since
                    // we might not need the full space reserved for the header, we modify the send buffer to ensure
                    // the first element points at the start of the Slic header. We'll restore the send buffer once
                    // the send is complete (it's important for the tracing code which might rely on the encoded
                    // data).
                    ReadOnlyMemory<byte> previous = buffers.Span[0];
                    Memory<byte> headerData = MemoryMarshal.AsMemory(
                        buffers.Span[0][(SlicDefinitions.FrameHeader.Length - sizeLength - streamIdLength - 1)..]);
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

        private static void WriteParameters(IceEncoder iceEncoder, Dictionary<ParameterKey, ulong> parameters)
        {
            iceEncoder.WriteSize(parameters.Count);
            foreach ((ParameterKey key, ulong value) in parameters)
            {
                iceEncoder.WriteField((int)key, value, BasicIceEncodeActions.VarULongIceEncodeAction);
            }
        }

        private static Dictionary<ParameterKey, ulong> ReadParameters(IceDecoder iceDecoder)
        {
            int dictionarySize = iceDecoder.DecodeSize();
            var parameters = new Dictionary<ParameterKey, ulong>();
            for (int i = 0; i < dictionarySize; ++i)
            {
                (int key, ReadOnlyMemory<byte> value) = iceDecoder.DecodeField();
                parameters.Add((ParameterKey)key, value.Span.DecodeVarULong().Value);
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
            using IMemoryOwner<byte> bufferOwner = MemoryPool<byte>.Shared.Rent(size);
            await ReceiveDataAsync(bufferOwner.Memory[0..size], cancel).ConfigureAwait(false);
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

            if (IsServer && peerIdleTimeout == null)
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

        private async ValueTask<(SlicDefinitions.FrameType, ReadOnlyMemory<byte>)> ReceiveFrameAsync(
            CancellationToken cancel)
        {
            (SlicDefinitions.FrameType type, int size) = await ReceiveHeaderAsync(cancel).ConfigureAwait(false);
            Memory<byte> data;
            if (size > 0)
            {
                data = new byte[size];
                await ReceiveDataAsync(data, cancel).ConfigureAwait(false);
            }
            else
            {
                data = Memory<byte>.Empty;
            }
            return (type, data);
        }

        private async ValueTask<(SlicDefinitions.FrameType, int)> ReceiveHeaderAsync(CancellationToken cancel)
        {
            // Receive at most 2 bytes for the Slic header (the minimum size of a Slic header). The first byte
            // will be the frame type and the second is the first byte of the Slic frame size.
            ReadOnlyMemory<byte> buffer = await _bufferedConnection!.ReceiveAsync(2, cancel).ConfigureAwait(false);
            var type = (SlicDefinitions.FrameType)buffer.Span[0];
            int sizeLength = buffer.Span[1].DecodeSizeLength20();
            int size;
            if (sizeLength > 1)
            {
                Received(buffer.Slice(0, 1));
                _bufferedConnection!.Rewind(1);
                buffer = await _bufferedConnection!.ReceiveAsync(sizeLength, cancel).ConfigureAwait(false);
                size = buffer.Span.DecodeSize20().Size;
                Received(buffer.Slice(0, sizeLength));
            }
            else
            {
                size = buffer.Span[1..2].DecodeSize20().Size;
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
            (ulong streamId, int streamIdLength) = buffer.Span.DecodeVarULong();
            _bufferedConnection!.Rewind(size - streamIdLength);
            Received(buffer[0..streamIdLength]);

            // Return the stream ID and the size of the data remaining to read for the frame.
            return ((long)streamId, frameSize - streamIdLength);
        }

        private async ValueTask WaitForReceivedStreamDataCompletionAsync(CancellationToken cancel)
        {
            // If the stream didn't fully read the stream data, finish reading it here before returning. The stream
            // might not have fully received the data if it was aborted or canceled.
            int size;
            ValueTask<int> receiveStreamCompletionTask = _receiveStreamCompletionTaskSource.ValueTask;
            if (receiveStreamCompletionTask.IsCompletedSuccessfully)
            {
                size = receiveStreamCompletionTask.Result;
            }
            else
            {
                size = await receiveStreamCompletionTask.AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }

            if (size > 0)
            {
                await IgnoreDataAsync(size, cancel).ConfigureAwait(false);
            }
        }
    }
}
