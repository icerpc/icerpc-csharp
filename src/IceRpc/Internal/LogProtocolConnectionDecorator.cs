// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A protocol connection enables communication over a network connection using either the Ice1
    /// or Ice2 protocol.</summary>
    internal class LogProtocolConnectionDecorator : IProtocolConnection
    {
        bool IProtocolConnection.HasDispatchInProgress => _decoratee.HasDispatchInProgress;
        bool IProtocolConnection.HasInvocationsInProgress => _decoratee.HasInvocationsInProgress;

        private readonly IProtocolConnection _decoratee;
        private readonly ILogger _logger;

        void IProtocolConnection.CancelInvocationsAndDispatches() => _decoratee.CancelInvocationsAndDispatches();

        void IDisposable.Dispose() => _decoratee.Dispose();

        Task IProtocolConnection.PingAsync(CancellationToken cancel) => _decoratee.PingAsync(cancel);

        Task<IncomingRequest> IProtocolConnection.ReceiveRequestAsync(CancellationToken cancel) =>
            _decoratee.ReceiveRequestAsync(cancel);

        Task<IncomingResponse> IProtocolConnection.ReceiveResponseAsync(
            OutgoingRequest request,
            CancellationToken cancel) =>
            _decoratee.ReceiveResponseAsync(request, cancel);

        Task IProtocolConnection.SendRequestAsync(OutgoingRequest request, CancellationToken cancel) =>
            _decoratee.SendRequestAsync(request, cancel);

        Task IProtocolConnection.SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel) =>
            _decoratee.SendResponseAsync(response, request, cancel);

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
