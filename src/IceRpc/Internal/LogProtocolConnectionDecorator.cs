// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
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

        private readonly NetworkConnectionInformation _connectionInformation;
        private readonly IProtocolConnection _decoratee;
        private readonly bool _isServer;
        private readonly ILogger _logger;

        void IProtocolConnection.ShutdownCanceled() => _decoratee.ShutdownCanceled();

        void IDisposable.Dispose() => _decoratee.Dispose();

        async Task IProtocolConnection.PingAsync(CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_connectionInformation, _isServer);
            await _decoratee.PingAsync(cancel).ConfigureAwait(false);
            _logger.LogPing();
        }

        async Task<IncomingRequest> IProtocolConnection.ReceiveRequestAsync()
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_connectionInformation, _isServer);
            IncomingRequest request = await _decoratee.ReceiveRequestAsync().ConfigureAwait(false);
            _logger.LogReceiveRequest(request.Path, request.Operation, request.PayloadSize, request.PayloadEncoding);
            return request;
        }

        async Task<IncomingResponse> IProtocolConnection.ReceiveResponseAsync(
            OutgoingRequest request,
            CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_connectionInformation, _isServer);
            using IDisposable _ = _logger.StartReceiveResponseScope(request);
            IncomingResponse response = await _decoratee.ReceiveResponseAsync(request, cancel).ConfigureAwait(false);

            _logger.LogReceiveResponse(response.PayloadSize, response.PayloadEncoding, response.ResultType);
            return response;
        }

        async Task IProtocolConnection.SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_connectionInformation, _isServer);
            using IDisposable _ = _logger.StartSendRequestScope(request);
            await _decoratee.SendRequestAsync(request, cancel).ConfigureAwait(false);
            _logger.LogSendRequest(request.PayloadSize, request.PayloadEncoding);
        }

        async Task IProtocolConnection.SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_connectionInformation, _isServer);
            using IDisposable _ = _logger.StartSendResponseScope(response, request);
            await _decoratee.SendResponseAsync(response, request, cancel).ConfigureAwait(false);
            _logger.LogSendResponse(response.PayloadSize, response.PayloadEncoding);
        }

        Task IProtocolConnection.ShutdownAsync(string message, CancellationToken cancel) =>
            _decoratee.ShutdownAsync(message, cancel);

        internal LogProtocolConnectionDecorator(
            IProtocolConnection decoratee,
            NetworkConnectionInformation connectionInformation,
            bool isServer,
            ILogger logger)
        {
            _decoratee = decoratee;
            _connectionInformation = connectionInformation;
            _isServer = isServer;
            _logger = logger;
        }
    }
}
