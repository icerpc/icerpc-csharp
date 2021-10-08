// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Slic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The Slic connection implements an <see cref="IMultiStreamConnection"/> on top of an <see
    /// cref="ISingleStreamConnection"/>.</summary>
    internal class SlicConnection : IMultiStreamConnection, IDisposable
    {
        internal TimeSpan IdleTimeout { get; set; }
        internal bool IsServer { get; }
        internal int PeerPacketMaxSize { get; private set; }
        internal int PeerStreamBufferMaxSize { get; private set; }
        internal int StreamBufferMaxSize { get; }

        private int _bidirectionalStreamCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private readonly int _bidirectionalMaxStreams;
        private long _lastRemoteBidirectionalStreamId = -1;
        private long _lastRemoteUnidirectionalStreamId = -1;
        // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
        // an atomic operation.
        private readonly object _mutex = new();
        private readonly int _packetMaxSize;
        private readonly ManualResetValueTaskCompletionSource<int> _receiveStreamCompletionTaskSource = new();
        private readonly ISlicFrameReader _reader;
        private readonly ConcurrentDictionary<long, SlicStream> _streams = new();
        private readonly int _unidirectionalMaxStreams;
        private int _unidirectionalStreamCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;
        private readonly ISlicFrameWriter _writer;

        public async ValueTask<INetworkStream> AcceptStreamAsync(CancellationToken cancel)
        {
            // Eventually wait for the stream data receive to complete if stream data is being received.
            await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);

            while (true)
            {
                (FrameType type, int dataSize, long streamId) =
                    await _reader.ReadStreamFrameHeaderAsync(cancel).ConfigureAwait(false);

                switch (type)
                {
                    case FrameType.Stream:
                    case FrameType.StreamLast:
                    {
                        bool endStream = type == FrameType.StreamLast;
                        if (dataSize == 0 && type == FrameType.Stream)
                        {
                            throw new InvalidDataException("received empty stream frame");
                        }

                        bool isRemote = streamId % 2 == (IsServer ? 0 : 1);
                        bool isBidirectional = streamId % 4 < 2;
                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            // Notify the stream that data is available for read.
                            stream.ReceivedFrame(dataSize, endStream);

                            // Wait for the stream to receive the data before reading a new Slic frame.
                            if (dataSize > 0)
                            {
                                await WaitForReceivedStreamDataCompletionAsync(cancel).ConfigureAwait(false);
                            }
                        }
                        else if (isRemote && !IsKnownRemoteStream(streamId, isBidirectional))
                        {
                            // Create a new stream if the incoming stream is unknown (the client could be
                            // sending frames for old canceled incoming streams, these are ignored).

                            if (dataSize == 0)
                            {
                                throw new InvalidDataException("received empty stream frame on new stream");
                            }

                            // Accept the new incoming stream and notify the stream that data is available.
                            try
                            {
                                stream = new SlicStream(this, streamId, _reader, _writer);
                            }
                            catch
                            {
                                // The connection is being closed, we make sure to receive the frame data.
                                // When the connection is being closed gracefully, the connection waits for
                                // the connection to receive the RST from the peer so it's important to
                                // receive and skip all the data until the RST is received.
                                await _reader.SkipStreamDataAsync(dataSize, cancel).ConfigureAwait(false);
                                continue;
                            }

                            if (isBidirectional)
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
                            stream.ReceivedFrame(dataSize, endStream);
                            return stream;
                        }
                        else if (!isBidirectional && endStream)
                        {
                            // Release the stream count for the unidirectional stream.
                            _unidirectionalStreamSemaphore!.Release();
                        }
                        else
                        {
                            throw new InvalidDataException($"received stream frame for unknown stream ID={streamId}");
                        }
                        break;
                    }
                    case FrameType.StreamConsumed:
                    {
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream consumed frame too large");
                        }

                        StreamConsumedBody streamConsumed =
                            await _reader.ReadStreamConsumedAsync(dataSize, cancel).ConfigureAwait(false);
                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            stream.ReceivedConsumed((int)streamConsumed.Size);
                        }
                        break;
                    }
                    case FrameType.StreamReset:
                    {
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream reset frame too large");
                        }

                        StreamResetBody streamReset =
                            await _reader.ReadStreamResetAsync(dataSize, cancel).ConfigureAwait(false);
                        if (TryGetStream(streamId, out SlicStream? stream))
                        {
                            stream.ReceivedReset((StreamError)streamReset.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case FrameType.StreamStopSending:
                    {
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream stop sending frame too large");
                        }

                        StreamStopSendingBody _ =
                            await _reader.ReadStreamStopSendingAsync(dataSize, cancel).ConfigureAwait(false);
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

            bool IsKnownRemoteStream(long streamId, bool bidirectional)
            {
                lock (_mutex)
                {
                    if (bidirectional)
                    {
                        return streamId <= _lastRemoteBidirectionalStreamId;
                    }
                    else
                    {
                        return streamId <= _lastRemoteUnidirectionalStreamId;
                    }
                }
            }
        }

        public INetworkStream CreateStream(bool bidirectional) => new SlicStream(this, bidirectional, _reader, _writer);

        public void Dispose()
        {
            // Abort streams.
            foreach (SlicStream stream in _streams.Values)
            {
                try
                {
                    ((INetworkStream)stream).Abort(StreamError.ConnectionAborted);
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"unexpected exception on Stream.Abort: {ex}");
                }
            }

            // Unblock requests waiting on the semaphores.
            var exception = new ConnectionClosedException();
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);

            _reader.Dispose();
            _writer.Dispose();
        }

        internal SlicConnection(
            ISlicFrameReader reader,
            ISlicFrameWriter writer,
            bool isServer,
            TimeSpan idleTimeout,
            SlicOptions options)
        {
            IdleTimeout = idleTimeout;
            IsServer = isServer;

            _reader = reader;
            _writer = new SynchronizedSlicFrameWriterDecorator(writer, isServer);

            _receiveStreamCompletionTaskSource.SetResult(0);

            _packetMaxSize = options.PacketMaxSize;
            StreamBufferMaxSize = options.StreamBufferMaxSize;

            // Initially set the peer packet max size to the local max size to ensure we can receive the first
            // initialize frame.
            PeerPacketMaxSize = _packetMaxSize;
            PeerStreamBufferMaxSize = StreamBufferMaxSize;

            // Configure the maximum stream counts to ensure the peer won't open more than one stream.
            _bidirectionalMaxStreams = options.BidirectionalStreamMaxCount;
            _unidirectionalMaxStreams = options.UnidirectionalStreamMaxCount;
        }

        internal void AddStream(long id, SlicStream stream, ref long streamId)
        {
            lock (_mutex)
            {
                _streams[id] = stream;

                // Assign the stream ID within the mutex to ensure that the addition of the stream to the
                // connection and the stream ID assignment are atomic.
                streamId = id;

                // Keep track of the last assigned stream ID. This is used to figure out if the stream is
                // known or unknown.
                if (stream.IsRemote)
                {
                    if (stream.IsBidirectional)
                    {
                        _lastRemoteBidirectionalStreamId = id;
                    }
                    else
                    {
                        _lastRemoteUnidirectionalStreamId = id;
                    }
                }
            }
        }

        internal void FinishedReceivedStreamData(int remainingSize)
        {
            Debug.Assert(!_receiveStreamCompletionTaskSource.IsCompleted);
            _receiveStreamCompletionTaskSource.SetResult(remainingSize);
        }

        internal async ValueTask InitializeAsync(CancellationToken cancel)
        {
            if (IsServer)
            {
                // Read the Initialize frame sent by the client.
                (uint version, InitializeBody? initializeBody) =
                    await _reader.ReadInitializeAsync(cancel).ConfigureAwait(false);
                if (version != 1)
                {
                    // Unsupported version, try to negotiate another version by sending a Version frame with
                    // the Slic versions supported by this server.
                    var versionBody = new VersionBody(new uint[] { SlicDefinitions.V1 });
                    await _writer.WriteVersionAsync(versionBody, cancel).ConfigureAwait(false);
                    (version, initializeBody) = await _reader.ReadInitializeAsync(cancel).ConfigureAwait(false);
                }

                if (initializeBody == null)
                {
                    throw new InvalidDataException($"unsupported Slic version '{version}'");
                }

                // Check the application protocol and set the parameters.
                try
                {
                    if (ProtocolParser.Parse(initializeBody.Value.ApplicationProtocolName) != Protocol.Ice2)
                    {
                        throw new NotSupportedException(
                            $"application protocol '{initializeBody.Value.ApplicationProtocolName}' is not supported");
                    }
                }
                catch (FormatException ex)
                {
                    throw new NotSupportedException(
                        $"unknown application protocol '{initializeBody.Value.ApplicationProtocolName}'", ex);
                }
                SetParameters(initializeBody.Value.Parameters);

                // Write back an InitializeAck frame.
                var initializeAck = new InitializeAckBody(GetParameters());
                await _writer.WriteInitializeAckAsync(initializeAck, cancel).ConfigureAwait(false);
            }
            else
            {
                // Write the Initialize frame.
                var initializeBody = new InitializeBody(Protocol.Ice2.GetName(), GetParameters());
                await _writer.WriteInitializeAsync(SlicDefinitions.V1, initializeBody, cancel).ConfigureAwait(false);

                // Read back either the InitializeAck or Version frame.
                (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                    await _reader.ReadInitializeAckOrVersionAsync(cancel).ConfigureAwait(false);

                if (initializeAckBody != null)
                {
                    SetParameters(initializeAckBody.Value.Parameters);
                }
                else if (versionBody != null)
                {
                    // We currently only support V1
                    throw new InvalidDataException(
                        $"unsupported Slic versions '{string.Join(", ", versionBody.Value.Versions)}'");
                }
            }
        }

        internal void ReleaseStream(SlicStream stream)
        {
            if (stream.IsRemote)
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

        internal void RemoveStream(long id) => _streams.TryRemove(id, out SlicStream? _);

        internal async ValueTask SendStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            AsyncSemaphore streamSemaphore = stream.IsBidirectional ?
                _bidirectionalStreamSemaphore! :
                _unidirectionalStreamSemaphore!;

            if (!stream.IsStarted)
            {
                // If the outgoing stream isn't started, we need to acquire the stream semaphore to ensure we
                // don't open more streams than the peer allows.
                await streamSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            }

            try
            {
                await _writer.WriteStreamFrameAsync(stream, buffers, endStream, cancel).ConfigureAwait(false);
            }
            finally
            {
                if (!stream.IsStarted)
                {
                    // If the stream is still not started, release the semaphore.
                    streamSemaphore.Release();
                }
            }
        }

        private Dictionary<int, IList<byte>> GetParameters()
        {
            return new Dictionary<int, IList<byte>>(new List<KeyValuePair<int, IList<byte>>>
                {
                    EncodeParameter(ParameterKey.MaxBidirectionalStreams, (ulong)_bidirectionalMaxStreams),
                    EncodeParameter(ParameterKey.MaxUnidirectionalStreams, (ulong)_unidirectionalMaxStreams),
                    EncodeParameter(ParameterKey.IdleTimeout, (ulong)IdleTimeout.TotalMilliseconds),
                    EncodeParameter(ParameterKey.PacketMaxSize, (ulong)_packetMaxSize),
                    EncodeParameter(ParameterKey.StreamBufferMaxSize, (ulong)StreamBufferMaxSize)
                });

            static KeyValuePair<int, IList<byte>> EncodeParameter(ParameterKey key, ulong value)
            {
                var bufferWriter = new BufferWriter();
                var encoder = new Ice20Encoder(bufferWriter);
                encoder.EncodeVarULong(value);
                return new((int)key, bufferWriter.Finish().ToSingleBuffer().ToArray());
            }
        }

        private void SetParameters(IDictionary<int, IList<byte>>  parameters)
        {
            TimeSpan? peerIdleTimeout = null;

            foreach ((ParameterKey key, ulong value) in parameters.DecodedParameters())
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
                        IdleTimeout = peerIdleTimeout.Value;
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

        private bool TryGetStream(long streamId, [NotNullWhen(returnValue: true)] out SlicStream? value)
        {
            if (_streams.TryGetValue(streamId, out SlicStream? stream))
            {
                value = stream;
                return true;
            }
            value = null;
            return false;
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
                // Skip the next size bytes from the stream.
                await _reader.SkipStreamDataAsync(size, cancel).ConfigureAwait(false);
            }
        }
    }
}
