// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connections.</summary>
    internal class LogProtocolConnectionDecorator : IProtocolConnection
    {
        bool IProtocolConnection.HasDispatchesInProgress => _decoratee.HasDispatchesInProgress;
        bool IProtocolConnection.HasInvocationsInProgress => _decoratee.HasInvocationsInProgress;

        private readonly IProtocolConnection _decoratee;
        private readonly ILogger _logger;

        void IProtocolConnection.CancelInvocationsAndDispatches() => _decoratee.CancelInvocationsAndDispatches();

        void IDisposable.Dispose() => _decoratee.Dispose();

        Task IProtocolConnection.PingAsync(CancellationToken cancel) => _decoratee.PingAsync(cancel);

        async Task<IncomingRequest> IProtocolConnection.ReceiveRequestAsync(CancellationToken cancel)
        {
            IncomingRequest request = await _decoratee.ReceiveRequestAsync(cancel).ConfigureAwait(false);
            _logger.LogReceivedRequestFrame(request.Path,
                                            request.Operation,
                                            request.PayloadSize,
                                            request.PayloadEncoding);
            return request;
        }

        async Task<IncomingResponse> IProtocolConnection.ReceiveResponseAsync(
            OutgoingRequest request,
            CancellationToken cancel)
        {
            IncomingResponse response = await _decoratee.ReceiveResponseAsync(request, cancel).ConfigureAwait(false);

            _logger.LogReceivedResponseFrame(request.Path,
                                             request.Operation,
                                             response.PayloadSize,
                                             response.PayloadEncoding,
                                             response.ResultType);
            return response;
        }

        async Task IProtocolConnection.SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            await _decoratee.SendRequestAsync(request, cancel).ConfigureAwait(false);
            _logger.LogSentRequestFrame(request.Path, request.Operation, request.PayloadSize, request.PayloadEncoding);
        }

        async Task IProtocolConnection.SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel)
        {
            await _decoratee.SendResponseAsync(response, request, cancel).ConfigureAwait(false);
            _logger.LogSentResponseFrame(request.Path,
                                         request.Operation,
                                         response.PayloadSize,
                                         response.PayloadEncoding,
                                         response.ResultType);
        }

        Task IProtocolConnection.ShutdownAsync(bool shutdownByPeer, string message, CancellationToken cancel) =>
            _decoratee.ShutdownAsync(shutdownByPeer, message, cancel);

        Task<string> IProtocolConnection.WaitForShutdownAsync(CancellationToken cancel) =>
            _decoratee.WaitForShutdownAsync(cancel);

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
