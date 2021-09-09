// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Protocols
{
    internal class MultiStreamProtocolConnection : IProtocolConnection
    {
        /// <inheritdoc/>
        public TimeSpan IdleTimeout
        {
            get => _transportConnection.IdleTimeout;
            private set => _transportConnection.IdleTimeout = value;
        }

        /// <inheritdoc/>
        public TimeSpan LastActivity => _transportConnection.LastActivity;

        public ITransportConnection TransportConnection => _transportConnection;

        private TaskCompletionSource? _cancelGoAwaySource;
        private RpcStream? _controlStream;
        private readonly int _incomingFrameSizeMax;
        private RpcStream? _peerControlStream;
        private readonly Action? _pingReceived;

        private readonly MultiStreamConnection _transportConnection;

        /// <summary>Creates a multi-stream protocol connection.</summary>
        public MultiStreamProtocolConnection(
            MultiStreamConnection transportConnection,
            TimeSpan idleTimeout,
            int incomingFrameMaxSize,
            Action? pingReceived)
        {
            _transportConnection = transportConnection;
            IdleTimeout = idleTimeout;
            _incomingFrameSizeMax = incomingFrameMaxSize;
            _pingReceived = pingReceived;

            // TODO: fix
            _transportConnection.IncomingFrameMaxSize = incomingFrameMaxSize;
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancel)
        {
            await _transportConnection.ConnectAsync(cancel).ConfigureAwait(false);

            if (!_transportConnection.IsDatagram)
            {
                // Create the control stream and send the protocol initialize frame
                _controlStream = await SendInitializeAsync(cancel).ConfigureAwait(false);

                // Wait for the peer control stream to be accepted and read the protocol initialize frame
                _peerControlStream = await ReceiveInitializeAsync(cancel).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void CancelShutdown()
        {
            if (_transportConnection.Protocol == Protocol.Ice1)
            {
                // Cancel dispatch if shutdown is canceled.
                _transportConnection?.CancelDispatch();
            }
            else
            {
                // Notify the task completion source that shutdown was canceled. PerformShutdownAsync will
                // send the GoAwayCanceled frame once the GoAway frame has been sent.
                _cancelGoAwaySource?.TrySetResult();
            }
        }

        public void Dispose()
        {
            _cancelGoAwaySource?.TrySetCanceled();
            _transportConnection.Dispose();
        }

        public Task PingAsync(CancellationToken cancel) => _transportConnection.PingAsync(cancel);

        /// <inheritdoc/>
        public async Task<IncomingRequest?> ReceiveRequestAsync(CancellationToken cancel)
        {
            // try
            {
                // Accepts a new stream.
                RpcStream stream = await _transportConnection!.AcceptStreamAsync(cancel).ConfigureAwait(false);

                // Receives the request frame from the stream. TODO: Only read the request header, the payload
                // should be received by calling IProtocolStream.ReceivePayloadAsync from the incoming frame
                // classes.
                IncomingRequest request = await stream.ReceiveRequestFrameAsync(cancel).ConfigureAwait(false);
                request.Stream = stream; // TODO: wrap the stream with IProtocolStream
                return request;
            }
            // TODO XXX
            // catch (ConnectionLostException) when (_controlStream!.WriteCompleted)
            // {
            //     // The control stream has been closed and the peer closed the connection. This indicates graceful
            //     // connection closure.
            //     _acceptStreamTaskCompletion.SetResult();
            //     return null;
            // }
            // catch (Exception exception)
            // {
            //     _acceptStreamTaskCompletion.SetException(exception);
            //     throw;
            // }
        }

        /// <inheritdoc/>
        public Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.IsOneway)
            {
                throw new InvalidOperationException("can't receive a response for a one-way request");
            }
            return request.Stream.ReceiveResponseFrameAsync(request, cancel).AsTask();
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // Creates the stream.
            request.Stream = _transportConnection!.CreateStream(!request.IsOneway);

            // Sends the request and wait for the sending to complete.
            await request.Stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

            // Mark the request as sent.
            request.IsSent = true;
        }

        /// <inheritdoc/>
        public Task SendResponseAsync(IncomingRequest request, OutgoingResponse response, CancellationToken cancel)
        {
            if (request.IsOneway)
            {
                throw new InvalidOperationException("can't send a response for a one-way request");
            }
            return request.Stream.SendResponseFrameAsync(response, cancel).AsTask();
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(bool shutdownByPeer, string message, CancellationToken cancel)
        {
            // Shutdown the multi-stream connection to prevent new streams from being created. This is done
            // before the yield to ensure consistency between the connection shutdown state and the connection
            // closing State.
            (long, long) lastIncomingStreamIds = _transportConnection.Shutdown();

            // Yield before continuing to ensure the code below isn't executed with the mutex locked
            // and that _closeTask is assigned before any synchronous continuations are ran.
            // TODO: XXX
            // await Task.Yield();

            if (!shutdownByPeer)
            {
                _cancelGoAwaySource = new();
            }

            if (_transportConnection.Protocol == Protocol.Ice1)
            {
                if (shutdownByPeer)
                {
                    Debug.Assert(_transportConnection.IncomingStreamCount == 0 &&
                                 _transportConnection.OutgoingStreamCount == 0);
                }
                else
                {
                    // Abort outgoing streams (except the control stream)
                    _transportConnection.AbortOutgoingStreams(
                        RpcStreamError.ConnectionShutdown,
                        (0, _transportConnection.IsServer ? 3 : 2));

                    // Abort the peer control stream.
                    _peerControlStream!.AbortRead(RpcStreamError.ConnectionShutdown);

                    // Wait for incoming streams to complete before sending the CloseConnetion frame. Ice1 doesn't
                    // support sending the largest request ID with the CloseConnection frame. When the peer
                    // receives the CloseConnection frame, it indicates that no more requests will be dispatch and
                    // the peer can therefore cancel remaining pending invocations (which can safely be retried).
                    if (_transportConnection.IncomingStreamCount > 0)
                    {
                        await _transportConnection.WaitForStreamCountAsync(int.MaxValue, 0, cancel).ConfigureAwait(false);
                    }

                    // Write the GoAway frame
                    await _controlStream!.SendGoAwayFrameAsync(lastIncomingStreamIds,
                                                               message,
                                                               cancel).ConfigureAwait(false);
                }
            }
            else
            {
                // Send GoAway frame
                await _controlStream!.SendGoAwayFrameAsync(lastIncomingStreamIds,
                                                           message,
                                                           cancel).ConfigureAwait(false);

                if (shutdownByPeer)
                {
                    // Wait for the GoAwayCanceled frame from the peer and cancel dispatch if received.
                    _ = WaitForGoAwayCanceledOrCloseAsync(cancel);
                }
                else
                {
                    // If shutdown is canceled, send the GoAwayCanceled frame and cancel local dispatch.
                    _ = SendGoAwayCancelIfShutdownCanceledAsync();
                }

                // Wait for all the streams to complete (expect the local control streams which are closed
                // once all the other streams are closed).
                await _transportConnection.WaitForStreamCountAsync(1, 1, cancel).ConfigureAwait(false);

                // Abort the control stream.
                _controlStream.AbortWrite(RpcStreamError.ConnectionShutdown);

                // Wait for the streams to be closed.
                await _transportConnection.WaitForStreamCountAsync(0, 0, cancel).ConfigureAwait(false);
            }

            async Task SendGoAwayCancelIfShutdownCanceledAsync()
            {
                try
                {
                    // Wait for the shutdown cancellation.
                    await _cancelGoAwaySource!.Task.ConfigureAwait(false);

                    // Cancel dispatch if shutdown is canceled.
                    _transportConnection!.CancelDispatch();

                    // Write the GoAwayCanceled frame to the peer's streams.
                    await _controlStream!.SendGoAwayCanceledFrameAsync().ConfigureAwait(false);
                }
                catch (RpcStreamAbortedException)
                {
                    // Expected if the control stream is closed.
                }
            }

            async Task WaitForGoAwayCanceledOrCloseAsync(CancellationToken cancel)
            {
                try
                {
                    // Wait to receive the GoAwayCanceled frame.
                    await _peerControlStream!.ReceiveGoAwayCanceledFrameAsync(cancel).ConfigureAwait(false);

                    // Cancel the dispatch if the peer canceled the shutdown.
                    _transportConnection!.CancelDispatch();
                }
                catch (RpcStreamAbortedException)
                {
                    // Expected if the control stream is closed.
                }
            }
        }

        public async Task<string> WaitForShutdownAsync(CancellationToken cancel)
        {
            ((long, long) lastOutgoingStreamIds, string message) =
                await _peerControlStream!.ReceiveGoAwayFrameAsync(cancel).ConfigureAwait(false);

            // Abort non-processed outgoing streams before closing the connection to ensure the invocations
            // will fail with a retryable exception.
            _transportConnection.AbortOutgoingStreams(RpcStreamError.ConnectionShutdownByPeer,
                                                      lastOutgoingStreamIds);

            return message;
        }

        private async ValueTask<RpcStream> ReceiveInitializeAsync(CancellationToken cancel)
        {
            if (_transportConnection.Protocol == Protocol.Ice1 &&
                _transportConnection.IsServer &&
                _transportConnection is Transports.Internal.Ice1Connection ice1Connection)
            {
                // With Ice1, the connection validation message is only sent by the server to the client. So here we
                // only expect the connection validation message for a client connection and just return the
                // control stream immediately for a server connection.
                return new Transports.Internal.Ice1Stream(ice1Connection, 2);
            }
            else
            {
                RpcStream stream = await _transportConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);
                await stream.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
                return stream;
            }
        }

        private async ValueTask<RpcStream> SendInitializeAsync(CancellationToken cancel)
        {
            if (_transportConnection.Protocol == Protocol.Ice1 &&
                !_transportConnection.IsServer &&
                _transportConnection is Transports.Internal.Ice1Connection ice1Connection)
            {
                // With Ice1, the connection validation message is only sent by the server to the client. So here
                // we only expect the connection validation message for a server connection and just return the
                // control stream immediately for a client connection.
                return new Transports.Internal.Ice1Stream(ice1Connection, ice1Connection.AllocateId(false));
            }
            else
            {
                RpcStream stream = _transportConnection.CreateStream(bidirectional: false);
                await stream.SendInitializeFrameAsync(cancel).ConfigureAwait(false);
                return stream;
            }
        }
    }
}
