// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Protocols
{
    public class MultiStreamProtocolConnection : IProtocolConnection
    {
        private TaskCompletionSource? _cancelGoAwaySource;
        private readonly MultiStreamConnection _transport;
        // The Ice2eam is assigned on the connection initialization and is immutable once the connection reaches
        // the Active state.
        private RpcStream? _controlStream;
        private TimeSpan _idleTimeout;
        private int _incomingFrameSizeMax;
        private RpcStream? _peerControlStream;
        private readonly TaskCompletionSource _acceptStreamCompletion = new();

        public MultiStreamProtocolConnection(
            MultiStreamConnection transport,
            TimeSpan idleTimeout,
            int incomingFrameSizeMax)
        {
            _transport = transport;
            _idleTimeout = idleTimeout;
            _incomingFrameSizeMax = incomingFrameSizeMax;
        }

        public async ValueTask InitializeAsync(CancellationToken cancel)
        {
            await _transport.InitializeAsync(cancel).ConfigureAwait(false);

            if (!_transport.IsDatagram)
            {
                // Create the control stream and send the protocol initialize frame
                _controlStream = await _transport.SendInitializeFrameAsync(
                    cancel).ConfigureAwait(false);

                // Wait for the peer control stream to be accepted and read the protocol initialize frame
                _peerControlStream = await _transport.ReceiveInitializeFrameAsync(
                    cancel).ConfigureAwait(false);
            }
        }

        public async ValueTask<IncomingRequest?> ReceiveRequestFrameAsync(CancellationToken cancel)
        {
            // Accept a new stream.
            try
            {
                RpcStream stream = await _transport!.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);

                // Get the cancellation token for the dispatch. The token is cancelled when the stream is reset by the peer
                // or when the stream is aborted because the connection shutdown is canceled or failed.
                CancellationToken cancel = stream.CancelDispatchSource!.Token;

                // Receives the request frame from the stream.
                IncomingRequest request = await stream.ReceiveRequestFrameAsync(cancel).ConfigureAwait(false);
                request.Stream = stream;

                _logger.LogReceivedRequestFrame(
                    request.Path,
                    request.Operation,
                    request.PayloadSize,
                    request.PayloadEncoding);
            }
            catch (ConnectionLostException) when (_controlStream!.WriteCompleted)
            {
                // The control stream has been closed and the peer closed the connection. This indicates graceful
                // connection closure.
                return null;
            }
        }

        public ValueTask<IncomingResponse> ReceiveResponseFrameAsync(
            OutgoingRequest request,
            CancellationToken cancel)
        {
            // Wait for the reception of the response.
            IncomingResponse response;
            if (request.IsOneway)
            {
                response = new IncomingResponse(Protocol, ResultType.Success)
                {
                    PayloadEncoding = request.PayloadEncoding,
                    Payload = default
                };
            }
            else
            {
                response = await request.Stream.ReceiveResponseFrameAsync(request, cancel).ConfigureAwait(false);
            }

            _logger.LogReceivedResponseFrame(
                request.Path,
                request.Operation,
                response.PayloadSize,
                response.PayloadEncoding,
                response.ResultType);

            response.Connection = this;

            return response;
        }

        public async ValueTask SendRequestFrameAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // Create the stream. The caller (the proxy InvokeAsync implementation) is responsible for releasing
            // the stream.
            request.Stream = _protocolConnection!.CreateStream(!request.IsOneway);

            // Send the request and wait for the sending to complete.
            await request.Stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

            _logger.LogSentRequestFrame(
                request.Path,
                request.Operation,
                request.PayloadSize,
                request.PayloadEncoding);

            // Mark the request as sent.
            request.IsSent = true;
        }

        public ValueTask SendResponseFrameAsync(
            IncomingRequest request,
            OutgoingResponse response,
            CancellationToken cancel) =>
            throw new NotImplementedException();

        ValueTask ShutdownAsync(Exception exception, CancellationToken cancel) =>
            throw new NotImplementedException();

        ValueTask WaitForShutdownAsync(CancellationToken cancel) =>
            throw new NotImplementedException();
    }
}
