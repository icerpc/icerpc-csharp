// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Security;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The MultiStreamSocket class for the colocated transport.</summary>
    internal class ColocatedSocket : MultiStreamSocket
    {
        public override TimeSpan IdleTimeout
        {
            get => Timeout.InfiniteTimeSpan;
            internal set => throw new NotSupportedException("IdleTimeout is not supported with colocated connections");
        }

        static private readonly object _pingFrame = new();
        private readonly AsyncSemaphore _bidirectionalStreamSemaphore;
        private readonly long _id;
        private readonly object _mutex = new();
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private AsyncSemaphore? _peerUnidirectionalStreamSemaphore;
        private readonly ChannelReader<(long, object?, bool)> _reader;
        private readonly AsyncSemaphore _unidirectionalStreamSemaphore;
        private readonly ChannelWriter<(long, object?, bool)> _writer;

        public override void Abort() => _writer.TryComplete();

        public override ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => default;

        public override async ValueTask<SocketStream> AcceptStreamAsync(CancellationToken cancel)
        {
            while (true)
            {
                try
                {
                    (long streamId, object? frame, bool fin) = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                    if (TryGetStream(streamId, out ColocatedStream? stream))
                    {
                        // If we received a frame for a known stream, signal the stream of the frame reception. A null
                        // frame indicates a stream reset so reset the stream in this case.
                        try
                        {
                            if (frame == null)
                            {
                                stream.ReceivedReset(0);
                            }
                            else
                            {
                                stream.ReceivedFrame(frame, fin);
                            }
                        }
                        catch
                        {
                            // Ignore the stream has been aborted.
                        }
                    }
                    else if (frame is IncomingRequestFrame || streamId == (IsIncoming ? 2 : 3))
                    {
                        // If we received an incoming request frame or a frame for the incoming control stream,
                        // create a new stream and provide it the received frame.
                        Debug.Assert(frame != null);
                        stream = new ColocatedStream(this, streamId);
                        try
                        {
                            stream.ReceivedFrame(frame, fin);
                            return stream;
                        }
                        catch
                        {
                            // Ignore, the stream got aborted.
                            stream.Release();
                        }
                    }
                    else if(streamId == -1)
                    {
                        Debug.Assert(frame == _pingFrame);
                        ReceivedPing();
                    }
                    else
                    {
                        // Canceled request, ignore
                    }
                }
                catch (ChannelClosedException exception)
                {
                    throw new ConnectionLostException(exception, RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
            }
        }

        public override ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => default;

        public override async ValueTask CloseAsync(Exception exception, CancellationToken cancel)
        {
            _writer.Complete();
            await _reader.Completion.WaitAsync(cancel).ConfigureAwait(false);
        }

        public override SocketStream CreateStream(bool bidirectional) =>
            // The first unidirectional stream is always the control stream
            new ColocatedStream(
                this,
                bidirectional,
                !bidirectional && (_nextUnidirectionalId == 2 || _nextUnidirectionalId == 3));

        public async override ValueTask InitializeAsync(CancellationToken cancel)
        {
            // Send our unidirectional semaphore to the peer. The peer will decrease the semaphore when the stream is
            // disposed.
            try
            {
                await _writer.WriteAsync((-1, _unidirectionalStreamSemaphore, false), cancel).ConfigureAwait(false);
                (_, object? semaphore, _) = await _reader.ReadAsync(cancel).ConfigureAwait(false);

                // Get the peer's unidirectional semaphore and keep track of it to be able to release it once an
                // unidirectional stream is disposed.
                _peerUnidirectionalStreamSemaphore = (AsyncSemaphore?)semaphore;
            }
            catch (Exception exception)
            {
                throw new TransportException(exception, RetryPolicy.AfterDelay(TimeSpan.Zero));
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
                throw new TransportException(exception, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
        }

        public override string ToString() =>
            $"colocated ID = {_id}\nserver = {((ColocatedEndpoint)Endpoint).Server.Name}\nincoming = {IsIncoming}";

        internal ColocatedSocket(
            ColocatedEndpoint endpoint,
            ILogger logger,
            int incomingFrameMaxSize,
            bool isIncoming,
            long id,
            ChannelWriter<(long, object?, bool)> writer,
            ChannelReader<(long, object?, bool)> reader)
            : base(endpoint, logger, incomingFrameMaxSize, isIncoming)
        {
            _id = id;
            _writer = writer;
            _reader = reader;

            if (isIncoming)
            {
                _bidirectionalStreamSemaphore = new AsyncSemaphore(endpoint.Communicator.BidirectionalStreamMaxCount);
                _unidirectionalStreamSemaphore = new AsyncSemaphore(endpoint.Communicator.UnidirectionalStreamMaxCount);
            }
            else
            {
                _bidirectionalStreamSemaphore = new AsyncSemaphore(endpoint.Server.BidirectionalStreamMaxCount);
                _unidirectionalStreamSemaphore = new AsyncSemaphore(endpoint.Server.UnidirectionalStreamMaxCount);
            }

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

        internal override (long, long) AbortStreams(Exception exception, Func<SocketStream, bool>? predicate)
        {
            (long, long) streamIds = base.AbortStreams(exception, predicate);

            // Unblock requests waiting on the semaphores.
            var ex = new ConnectionClosedException(isClosedByPeer: false, RetryPolicy.AfterDelay(TimeSpan.Zero));
            _bidirectionalStreamSemaphore.Complete(ex);
            _unidirectionalStreamSemaphore.Complete(ex);

            return streamIds;
        }

        internal void ReleaseStream(ColocatedStream stream)
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
            ColocatedStream stream,
            object? frame,
            bool fin,
            CancellationToken cancel)
        {
            if (stream.IsStarted)
            {
                try
                {
                    await _writer.WriteAsync((stream.Id, frame, fin), cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
            }
            else
            {
                Debug.Assert(!stream.IsIncoming);

                if (!stream.IsControl)
                {
                    // Wait on the stream semaphore to ensure that we don't open more streams than the peer
                    // allows. The wait is done on the client side to ensure the sent callback for the request
                    // isn't called until the server is ready to dispatch a new request.
                    AsyncSemaphore semaphore = stream.IsBidirectional ?
                        _bidirectionalStreamSemaphore : _unidirectionalStreamSemaphore;
                    await semaphore.EnterAsync(cancel).ConfigureAwait(false);
                }

                // Ensure we allocate and queue the first stream frame atomically to ensure the receiver won't
                // receive stream frames with out-of-order stream IDs.
                try
                {
                    ValueTask task;
                    lock (_mutex)
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

                        // The write is not cancelable here, we want to make sure that at this point the peer
                        // receives the frame in order for the serialization semaphore to be released (otherwise,
                        // the peer would receive a reset for a stream for which it never received a frame).
                        task = _writer.WriteAsync((stream.Id, frame, fin), cancel);
                    }
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
            }
        }

        internal override IDisposable? StartScope()
        {
            // If any of the loggers is enabled we create the scope
            if (Logger.IsEnabled(LogLevel.Critical) ||
                Endpoint.Communicator.ProtocolLogger.IsEnabled(LogLevel.Critical) ||
                Endpoint.Communicator.SecurityLogger.IsEnabled(LogLevel.Critical) ||
                Endpoint.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Critical) ||
                Endpoint.Communicator.Logger.IsEnabled(LogLevel.Critical))
            {
                return Endpoint.Communicator.Logger.StartColocatedSocketScope(
                    _id,
                    ((ColocatedEndpoint)Endpoint).Server.Name);
            }
            return null;
        }
    }
}
