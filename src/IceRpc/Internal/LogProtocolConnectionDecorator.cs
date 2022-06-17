// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connections.</summary>
    internal class LogProtocolConnectionDecorator : IProtocolConnection
    {
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

        async Task<NetworkConnectionInformation> IProtocolConnection.ConnectAsync(
            IConnection connection,
            bool isServer,
            CancellationToken cancel)
        {
            _isServer = isServer;
            _information = await _decoratee.ConnectAsync(connection, isServer, cancel).ConfigureAwait(false);

            using IDisposable scope = _logger.StartConnectionScope(_information, isServer);
            _logger.LogProtocolConnectionConnect(
                _decoratee.Protocol,
                _information.LocalEndPoint,
                _information.RemoteEndPoint);

            _decoratee.OnClose(
                exception =>
                {
                    using IDisposable scope = _logger.StartClientConnectionScope(_information);
                    _logger.LogConnectionClosedReason(exception);
                });

            return _information;
        }

        async Task<NetworkConnectionInformation> IProtocolConnection.ConnectAsync(
            bool isServer,
            Action onIdle,
            Action<string> onShutdown,
            CancellationToken cancel)
        {
            _isServer = isServer;
            _information = await _decoratee.ConnectAsync(isServer, onIdle, onShutdown, cancel).ConfigureAwait(false);

            using IDisposable scope = _logger.StartConnectionScope(_information, isServer);
            _logger.LogProtocolConnectionConnect(
                _decoratee.Protocol,
                _information.LocalEndPoint,
                _information.RemoteEndPoint);

            return _information;
        }

        async Task<IncomingResponse> IProtocolConnection.InvokeAsync(
            IConnection connection,
            OutgoingRequest request,
            CancellationToken cancel)
        {
            using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
            using IDisposable _ = _logger.StartSendRequestScope(request);
            IncomingResponse response = await _decoratee.InvokeAsync(
                connection,
                request,
                cancel).ConfigureAwait(false);
            _logger.LogSendRequest();
            return response;
        }

        void IProtocolConnection.OnClose(Action<Exception> callback) => _decoratee.OnClose(callback);

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
