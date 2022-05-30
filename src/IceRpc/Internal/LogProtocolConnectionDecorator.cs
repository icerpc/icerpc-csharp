// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connections.</summary>
    internal class LogProtocolConnectionDecorator : IProtocolConnection
    {
        Action<string>? IProtocolConnection.InitiateShutdown
        {
            get => _decoratee.InitiateShutdown;
            set => _decoratee.InitiateShutdown = value;
        }

        Protocol IProtocolConnection.Protocol => _decoratee.Protocol;

        private readonly IProtocolConnection _decoratee;
        private NetworkConnectionInformation _information;
        private bool _isServer;
        private readonly ILogger _logger;

        public void Abort(Exception exception)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _decoratee.Abort(exception);
            _logger.LogProtocolConnectionAbort(_decoratee.Protocol, exception);
        }

        async Task IProtocolConnection.AcceptRequestsAsync(IConnection connection)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _logger.LogAcceptRequests();
            await _decoratee.AcceptRequestsAsync(connection).ConfigureAwait(false);
        }

        async Task<NetworkConnectionInformation> IProtocolConnection.ConnectAsync(
            bool isServer,
            CancellationToken cancel)
        {
            _isServer = isServer;
            _information = await _decoratee.ConnectAsync(isServer, cancel).ConfigureAwait(false);

            using IDisposable scope = _logger.StartConnectionScope(_information, isServer);
            _logger.LogProtocolConnectionConnect(
                _decoratee.Protocol,
                _information.LocalEndPoint,
                _information.RemoteEndPoint);

            return _information;
        }

        void IDisposable.Dispose()
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            _decoratee.Dispose();
            _logger.LogProtocolConnectionDispose(_decoratee.Protocol);
        }

        bool IProtocolConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        async Task<IncomingResponse> IProtocolConnection.InvokeAsync(
            OutgoingRequest request,
            IConnection connection,
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
                        _logger.LogProtocolConnectionShutdownCanceled(_decoratee.Protocol);
                    }
                    catch
                    {
                    }
                });
            _logger.LogProtocolConnectionShutdown(_decoratee.Protocol, message);
        }

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
