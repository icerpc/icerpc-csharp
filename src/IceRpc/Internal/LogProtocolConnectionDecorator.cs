// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connections.</summary>
    internal class LogProtocolConnectionDecorator : IProtocolConnection
    {
        bool IProtocolConnection.HasDispatchesInProgress => _decoratee.HasDispatchesInProgress;
        bool IProtocolConnection.HasInvocationsInProgress => _decoratee.HasInvocationsInProgress;

        ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> IProtocolConnection.PeerFields =>
            _decoratee.PeerFields;

        Action<string>? IProtocolConnection.PeerShutdownInitiated
        {
            get => _decoratee.PeerShutdownInitiated;
            set => _decoratee.PeerShutdownInitiated = value;
        }

        private readonly Protocol _protocol;
        private readonly IProtocolConnection _decoratee;
        private readonly NetworkConnectionInformation _information;
        private readonly bool _isServer;
        private readonly ILogger _logger;

        async Task IProtocolConnection.AcceptRequestsAsync(Connection connection)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _logger.LogAcceptRequests();
            await _decoratee.AcceptRequestsAsync(connection).ConfigureAwait(false);
        }

        void IDisposable.Dispose()
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _decoratee.Dispose();
            _logger.LogProtocolConnectionDispose(_protocol);
        }

        async Task IProtocolConnection.PingAsync(CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            await _decoratee.PingAsync(cancel).ConfigureAwait(false);
            _logger.LogPing();
        }

        async Task<IncomingResponse> IProtocolConnection.InvokeAsync(
            OutgoingRequest request,
            Connection connection,
            CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            using IDisposable _ = _logger.StartSendRequestScope(request);
            IncomingResponse response = await _decoratee.InvokeAsync(
                request,
                connection,
                cancel).ConfigureAwait(false);
            _logger.LogSendRequest();
            return response;
        }

        async Task IProtocolConnection.ShutdownAsync(string message, CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            await _decoratee.ShutdownAsync(message, cancel).ConfigureAwait(false);
            using CancellationTokenRegistration _ = cancel.Register(() =>
                {
                    try
                    {
                        _logger.LogProtocolConnectionShutdownCanceled(_protocol);
                    }
                    catch
                    {
                    }
                });
            _logger.LogProtocolConnectionShutdown(_protocol, message);
        }

        internal LogProtocolConnectionDecorator(
            IProtocolConnection decoratee,
            Protocol protocol,
            NetworkConnectionInformation connectionInformation,
            bool isServer,
            ILogger logger)
        {
            _protocol = protocol;
            _decoratee = decoratee;
            _information = connectionInformation;
            _isServer = isServer;
            _logger = logger;
        }
    }
}
