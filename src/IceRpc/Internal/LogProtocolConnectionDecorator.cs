// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>A log decorator for protocol connections.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    Protocol IProtocolConnection.Protocol => _decoratee.Protocol;

    private readonly IProtocolConnection _decoratee;
    private readonly Endpoint _endpoint;
    private TransportConnectionInformation _information;
    private readonly ILogger _logger;

    async Task<TransportConnectionInformation> IProtocolConnection.ConnectAsync(CancellationToken cancel)
    {
        try
        {
            _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logger.LogProtocolConnectionConnect(
                   _endpoint.Protocol,
                   _endpoint,
                   _information.LocalNetworkAddress,
                   _information.RemoteNetworkAddress);

            return _information;
        }
        catch (Exception exception)
        {
            _logger.LogProtocolConnectionConnectException(_endpoint.Protocol, _endpoint, exception);
            throw;
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);
        _logger.LogProtocolConnectionDispose(
            _endpoint.Protocol,
            _endpoint,
            _information.LocalNetworkAddress,
            _information.RemoteNetworkAddress);
    }

    Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _decoratee.InvokeAsync(request, cancel);

        /*
        using IDisposable connectionScope = _logger.StartConnectionScope(_information, _isServer);
        using IDisposable _ = _logger.StartSendRequestScope(request);
        IncomingResponse response = await _decoratee.InvokeAsync(request, cancel).ConfigureAwait(false);
        _logger.LogSendRequest();
        return response;
        */

    void IProtocolConnection.OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    void IProtocolConnection.OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

    async Task IProtocolConnection.ShutdownAsync(string message, CancellationToken cancel)
    {
        try
        {
            await _decoratee.ShutdownAsync(message, cancel).ConfigureAwait(false);
            _logger.LogProtocolConnectionShutdown(
                _endpoint.Protocol,
                _endpoint,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress,
                message);
        }
        catch (Exception exception)
        {
            _logger.LogProtocolConnectionShutdownException(
                _endpoint.Protocol,
                _endpoint,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress,
                exception);
            throw;
        }
    }

    internal LogProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        Endpoint endpoint,
        ILogger logger)
    {
        _decoratee = decoratee;
        _endpoint = endpoint;
        _logger = logger;
    }
}
