// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>A log decorator for protocol connections.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private readonly IProtocolConnection _decoratee;
    private TransportConnectionInformation _information;
    private readonly ILogger _logger;

    async Task<TransportConnectionInformation> IProtocolConnection.ConnectAsync(CancellationToken cancel)
    {
        try
        {
            _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logger.LogProtocolConnectionConnect(
                   Endpoint.Protocol,
                   Endpoint,
                   _information.LocalNetworkAddress,
                   _information.RemoteNetworkAddress);

            return _information;
        }
        catch (Exception exception)
        {
            _logger.LogProtocolConnectionConnectException(Endpoint.Protocol, Endpoint, exception);
            throw;
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);
        _logger.LogProtocolConnectionDispose(
            Endpoint.Protocol,
            Endpoint,
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
                Endpoint.Protocol,
                Endpoint,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress,
                message);
        }
        catch (Exception exception)
        {
            _logger.LogProtocolConnectionShutdownException(
                Endpoint.Protocol,
                Endpoint,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress,
                exception);
            throw;
        }
    }

    internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
