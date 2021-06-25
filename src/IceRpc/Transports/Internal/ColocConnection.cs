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
        /// <inheritdoc/>
        public override TimeSpan IdleTimeout
        {
            get => Timeout.InfiniteTimeSpan;
            internal set => throw new NotSupportedException("IdleTimeout is not supported with colocated connections");
        }

        /// <inheritdoc/>
        public long Id { get; }

        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation =>
            _connectionInformation ??= new ColocConnectionInformation { Id = Id };

        static private readonly object _pingFrame = new();
        private readonly int _bidirectionalStreamMaxCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private ColocConnectionInformation? _connectionInformation;
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
                try
                {
                    (long streamId, object frame, bool fin) = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                    if (streamId == -1)
                    {
                        if (frame == _pingFrame)
                        {
                            PingReceived?.Invoke();
                        }
                        else if (fin)
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
                            stream.ReceivedFrame(frame, fin);
                        }
                        catch
                        {
                            // Ignore the stream has been aborted.
                        }
                    }
                    else if (frame is IncomingRequest || streamId == (IsServer ? 2 : 3))
                    {
                        // If we received an incoming request frame or a frame for the incoming control stream,
                        // create a new stream and provide it the received frame.
                        Debug.Assert(frame != null);
                        try
                        {
                            stream = new ColocStream(this, streamId);
                            stream.ReceivedFrame(frame, fin);
                            return stream;
                        }
                        catch
                        {
                            // Ignore, the stream got aborted or the socket is being shutdown.
                        }
                    }
                    else
                    {
                        // Canceled request, ignore
                    }
                }
                catch (ChannelClosedException exception)
                {
                    throw new ConnectionLostException(exception);
                }
            }
        }

        public override ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => default;

        public override ValueTask CloseAsync(ConnectionErrorCode errorCode, CancellationToken cancel) =>
            _writer.WriteAsync((-1, errorCode, true), cancel);

        public override RpcStream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new ColocStream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public async override ValueTask InitializeAsync(CancellationToken cancel)
        {
            // Send our unidirectional semaphore to the peer. The peer will decrease the semaphore when the stream is
            // disposed.
            try
            {
                await _writer.WriteAsync((-1, this, false), cancel).ConfigureAwait(false);
                (_, object? peer, _) = await _reader.ReadAsync(cancel).ConfigureAwait(false);

                var peerSocket = (ColocConnection)peer!;

                // We're responsible for creating the peer's semaphores with our configured stream max count.
                peerSocket._bidirectionalStreamSemaphore = new AsyncSemaphore(_bidirectionalStreamMaxCount);
                peerSocket._unidirectionalStreamSemaphore = new AsyncSemaphore(_unidirectionalStreamMaxCount);
                _peerUnidirectionalStreamSemaphore = peerSocket._unidirectionalStreamSemaphore;
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
                _bidirectionalStreamSemaphore!.Complete(exception);
                _unidirectionalStreamSemaphore!.Complete(exception);
            }
        }

        internal ColocConnection(
            ColocEndpoint endpoint,
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
            bool fin,
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
                if (stream.AbortException is Exception exception)
                {
                    throw exception;
                }

                ValueTask task;
                lock (_mutex)
                {
                    // Allocate stream ID if the stream isn't started.
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

                    // Write the frame. It's important to allocate the ID and to send the frame within the
                    // synchronization block to ensure the reader won't receive frames with out-of-order
                    // stream IDs.
                    task = _writer.WriteAsync((stream.Id, frame, fin), cancel);
                }

                await task.ConfigureAwait(false);

                if (fin)
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
    }
}
