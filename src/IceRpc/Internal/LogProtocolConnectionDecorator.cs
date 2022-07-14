// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>A log decorator for protocol connections.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    Protocol IProtocolConnection.Protocol => _decoratee.Protocol;

    private readonly IProtocolConnection _decoratee;
    private TransportConnectionInformation _information;
    private readonly bool _isServer;
    private readonly ILogger _logger;

    async Task<TransportConnectionInformation> IProtocolConnection.ConnectAsync(CancellationToken cancel)
    {
        _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

        using IDisposable scope = _logger.StartConnectionScope(_information, _isServer);
        _logger.LogProtocolConnectionConnect(
            _decoratee.Protocol,
            _information.LocalNetworkAddress,
            _information.RemoteNetworkAddress);

        _decoratee.OnAbort(
            exception =>
            {
                using IDisposable scope = _logger.StartClientConnectionScope(_information);
                _logger.LogConnectionClosedReason(exception);
            });

        _decoratee.OnShutdown(
            message =>
            {
                using IDisposable scope = _logger.StartClientConnectionScope(_information);
                _logger.LogConnectionShutdownReason(message);
            });

        return _information;
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _decoratee.DisposeAsync();

    async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
        using IDisposable _ = _logger.StartSendRequestScope(request);
        IncomingResponse response = await _decoratee.InvokeAsync(request, cancel).ConfigureAwait(false);
        _logger.LogSendRequest();
        return response;
    }

    void IProtocolConnection.OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    void IProtocolConnection.OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

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

    internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, bool isServer, ILogger logger)
    {
        _decoratee = decoratee;
        _isServer = isServer;
        _logger = logger;
    }
}
