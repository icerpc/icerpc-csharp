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

        event Action<string>? IProtocolConnection.PeerShutdownInitiated
        {
            add => _decoratee.PeerShutdownInitiated += value;
            remove => _decoratee.PeerShutdownInitiated -= value;
        }

        private readonly IProtocolConnection _decoratee;
        private readonly NetworkConnectionInformation _information;
        private readonly bool _isServer;
        private readonly ILogger _logger;

        async Task IProtocolConnection.AcceptRequestsAsync(Connection connection, IDispatcher dispatcher)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _logger.LogAcceptRequests();
            await _decoratee.AcceptRequestsAsync(connection, dispatcher).ConfigureAwait(false);
        }

        void IDisposable.Dispose()
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _decoratee.Dispose();
            _logger.LogProtocolConnectionDispose(_information.LocalEndpoint.Protocol);
        }

        async Task IProtocolConnection.PingAsync(CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            await _decoratee.PingAsync(cancel).ConfigureAwait(false);
            _logger.LogPing();
        }

        async Task<IncomingResponse> IProtocolConnection.SendRequestAsync(
            Connection connection,
            OutgoingRequest request,
            CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            using IDisposable _ = _logger.StartSendRequestScope(request);
            IncomingResponse response = await _decoratee.SendRequestAsync(
                connection,
                request,
                cancel).ConfigureAwait(false);
            _logger.LogSendRequest(request.PayloadEncoding);
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
                        _logger.LogProtocolConnectionShutdownCanceled(_information.LocalEndpoint.Protocol);
                    }
                    catch
                    {
                    }
                });
            _logger.LogProtocolConnectionShutdown(_information.LocalEndpoint.Protocol, message);
        }

        internal LogProtocolConnectionDecorator(
            IProtocolConnection decoratee,
            NetworkConnectionInformation connectionInformation,
            bool isServer,
            ILogger logger)
        {
            _decoratee = decoratee;
            _information = connectionInformation;
            _isServer = isServer;
            _logger = logger;
        }
    }
}
