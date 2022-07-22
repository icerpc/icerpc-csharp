// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>A log decorator for protocol connections.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private readonly IProtocolConnection _decoratee;
    private IConnectionContext? _connectionContext;
    private readonly ILogger _logger;

    async Task<TransportConnectionInformation> IProtocolConnection.ConnectAsync(CancellationToken cancel)
    {
        try
        {
            TransportConnectionInformation information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logger.LogProtocolConnectionConnect(
                   Endpoint.Protocol,
                   Endpoint,
                   information.LocalNetworkAddress,
                   information.RemoteNetworkAddress);

            _connectionContext ??= new ConnectionContext(this, information);

            return information;
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
            _connectionContext?.TransportConnectionInformation.LocalNetworkAddress,
            _connectionContext?.TransportConnectionInformation.RemoteNetworkAddress);
    }

    async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IncomingResponse response = await _decoratee.InvokeAsync(request, cancel).ConfigureAwait(false);
        response.ConnectionContext = _connectionContext!;
        return response;
    }

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
                _connectionContext?.TransportConnectionInformation.LocalNetworkAddress,
                _connectionContext?.TransportConnectionInformation.RemoteNetworkAddress,
                message);
        }
        catch (Exception exception)
        {
            _logger.LogProtocolConnectionShutdownException(
                Endpoint.Protocol,
                Endpoint,
                _connectionContext?.TransportConnectionInformation.LocalNetworkAddress,
                _connectionContext?.TransportConnectionInformation.RemoteNetworkAddress,
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
