// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Security;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>The MultiStreamConnection class for the colocated transport.</summary>
    internal class ColocConnection : MultiStreamConnection
    {
        public override TimeSpan IdleTimeout
        {
            get => Timeout.InfiniteTimeSpan;
            internal set => throw new NotSupportedException("IdleTimeout is not supported with colocated connections");
        }

        public override bool IsDatagram => false;
        public override bool? IsSecure => true;

        internal long Id { get; }

        private static readonly object _pingFrame = new();
        private readonly int _bidirectionalStreamMaxCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private readonly object _mutex = new();
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private AsyncSemaphore? _peerUnidirectionalStreamSemaphore;
        private readonly ChannelReader<(long, object, bool)> _reader;
        private readonly int _unidirectionalStreamMaxCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;
        private readonly ChannelWriter<(long, object, bool)> _writer;

        public override ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => default;

        public override async ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel)
        {
            while (true)
            {
                long streamId;
                object frame;
                bool endStream;
                try
                {
                    (streamId, frame, endStream) = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                }
                catch (ChannelClosedException exception)
                {
                    throw new ConnectionLostException(exception);
                }

                bool isIncoming = streamId % 2 == (IsServer ? 0 : 1);
                bool isBidirectional = streamId % 4 < 2;

                // The -1 stream ID value indicates a connection frame, not belonging to any stream.
                if (streamId == -1)
                {
                    if (frame == _pingFrame)
                    {
                        PingReceived?.Invoke();
                    }
                    else if (endStream)
                    {
                        throw new ConnectionClosedException();
                    }
                    else
                    {
                        Debug.Assert(false);
                    }
                }
                else if (TryGetStream(streamId, out ColocStream? stream))
                {
                    try
                    {
                        stream.ReceivedFrame(frame, endStream);
                    }
                    catch
                    {
                        // Ignore, the stream got aborted possibly because the connection is being shutdown.
                    }
                }
                else if (isIncoming && IsIncomingStreamUnknown(streamId, isBidirectional))
                {
                    Debug.Assert(frame != null);
                    try
                    {
                        stream = new ColocStream(this, streamId);
                        stream.ReceivedFrame(frame, endStream);
                        return stream;
                    }
                    catch
                    {
                        // Ignore, the stream got aborted possibly because the connection is being shutdown.
                    }
                }
                else
                {
                    // Canceled request, ignore
                }
            }
        }

        public override ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => default;

        public override RpcStream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new ColocStream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public override async ValueTask InitializeAsync(CancellationToken cancel)
        {
            try
            {
                var initializeFrame = new InitializeFrame()
                {
                    BidirectionalStreamSemaphore = new AsyncSemaphore(_bidirectionalStreamMaxCount),
                    UnidirectionalStreamSemaphore = new AsyncSemaphore(_unidirectionalStreamMaxCount)
                };

                // We keep track of the peer's unidirectional stream semaphore. This side of the colocated connection
                // releases the peer's unidirectional stream semaphore when an incoming unidirectional stream is
                // released.
                _peerUnidirectionalStreamSemaphore = initializeFrame.UnidirectionalStreamSemaphore;

                await _writer.WriteAsync((-1, initializeFrame, false), cancel).ConfigureAwait(false);
                (_, object? frame, _) = await _reader.ReadAsync(cancel).ConfigureAwait(false);

                initializeFrame = (InitializeFrame)frame!;

                // Get the semphores from the initialize frame.
                _bidirectionalStreamSemaphore = initializeFrame.BidirectionalStreamSemaphore!;
                _unidirectionalStreamSemaphore = initializeFrame.UnidirectionalStreamSemaphore!;
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception exception)
            {
                throw new TransportException(exception);
            }
        }

        public override async Task PingAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                await _writer.WriteAsync((-1, _pingFrame, false), cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new TransportException(exception);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _writer.TryComplete();

                var exception = new ConnectionClosedException();
                _bidirectionalStreamSemaphore?.Complete(exception);
                _unidirectionalStreamSemaphore?.Complete(exception);
            }
        }

        internal ColocConnection(
            EndpointRecord endpoint,
            long id,
            ChannelWriter<(long, object, bool)> writer,
            ChannelReader<(long, object, bool)> reader,
            ConnectionOptions options,
            ILogger logger)
            : base(endpoint, options, logger)
        {
            LocalEndpoint = endpoint;
            RemoteEndpoint = endpoint;

            Id = id;
            _writer = writer;
            _reader = reader;

            _bidirectionalStreamMaxCount = options.BidirectionalStreamMaxCount;
            _unidirectionalStreamMaxCount = options.UnidirectionalStreamMaxCount;

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

        internal void ReleaseStream(ColocStream stream)
        {
            if (stream.IsIncoming && !stream.IsBidirectional && !stream.IsControl)
            {
                // This client side stream acquires the semaphore before opening an unidirectional stream. The
                // semaphore is released when this server side stream is disposed.
                _peerUnidirectionalStreamSemaphore?.Release();
            }
            else if (!stream.IsIncoming && stream.IsBidirectional && stream.IsStarted)
            {
                // This client side stream acquires the semaphore before opening a bidirectional stream. The
                // semaphore is released when this client side stream is disposed.
                _bidirectionalStreamSemaphore?.Release();
            }
        }

        internal async ValueTask SendFrameAsync(
            ColocStream stream,
            object frame,
            bool endStream,
            CancellationToken cancel)
        {
            AsyncSemaphore streamSemaphore = stream.IsBidirectional ?
                _bidirectionalStreamSemaphore! :
                _unidirectionalStreamSemaphore!;

            if (!stream.IsStarted && !stream.IsControl)
            {
                // Wait on the stream semaphore to ensure that we don't open more streams than the peer
                // allows. The wait is done on the client side to ensure the sent callback for the request
                // isn't called until the server is ready to dispatch a new request.
                await streamSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            }

            try
            {
                // If the stream is aborted, stop sending stream frames.
                if (stream.WriteCompleted && !(frame is RpcStreamError))
                {
                    if (!stream.IsStarted && !stream.IsControl)
                    {
                        streamSemaphore.Release();
                    }
                    throw new RpcStreamAbortedException(RpcStreamError.StreamAborted);
                }

                ValueTask task;
                lock (_mutex)
                {
                    // If the stream isn't started, allocate the stream ID (according to Quic numbering scheme)
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

                    // Write the frame. It's important to allocate the ID and to send the frame within the
                    // synchronization block to ensure the reader won't receive frames with out-of-order
                    // stream IDs.
                    task = _writer.WriteAsync((stream.Id, frame, endStream), cancel);
                }

                await task.ConfigureAwait(false);

                if (endStream)
                {
                    stream.TrySetWriteCompleted();
                }
            }
            catch (ChannelClosedException ex)
            {
                Debug.Assert(stream.IsStarted);
                throw new TransportException(ex);
            }
            catch
            {
                if (!stream.IsStarted && !stream.IsControl)
                {
                    streamSemaphore.Release();
                }
                throw;
            }
        }

        private sealed class InitializeFrame
        {
            internal AsyncSemaphore? BidirectionalStreamSemaphore { get; set; }
            internal AsyncSemaphore? UnidirectionalStreamSemaphore { get; set; }
        }
    }
}
