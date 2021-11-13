// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connections.</summary>
    internal class LogProtocolConnectionDecorator : IProtocolConnection
    {
        bool IProtocolConnection.HasDispatchesInProgress => _decoratee.HasDispatchesInProgress;
        bool IProtocolConnection.HasInvocationsInProgress => _decoratee.HasInvocationsInProgress;
        event Action? IProtocolConnection.PeerShutdownInitiated
        {
            add => _decoratee.PeerShutdownInitiated += value;
            remove => _decoratee.PeerShutdownInitiated -= value;
        }

        private readonly IProtocolConnection _decoratee;
        private readonly ILogger _logger;

        void IProtocolConnection.ShutdownCanceled() => _decoratee.ShutdownCanceled();

        void IDisposable.Dispose() => _decoratee.Dispose();

        Task IProtocolConnection.PingAsync(CancellationToken cancel) => _decoratee.PingAsync(cancel);

        async Task<IncomingRequest> IProtocolConnection.ReceiveRequestAsync()
        {
            IncomingRequest request = await _decoratee.ReceiveRequestAsync().ConfigureAwait(false);
            _logger.LogReceivedRequestFrame(request.Path,
                                            request.Operation,
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

        Task IProtocolConnection.ShutdownAsync(string message, CancellationToken cancel) =>
            _decoratee.ShutdownAsync(message, cancel);

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
