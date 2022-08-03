// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>A log decorator for protocol connections.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private readonly IProtocolConnection _decoratee;
    private TransportConnectionInformation _information;
    private readonly ILogger _logger;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformConnectAsync() : _decoratee.ConnectAsync(cancel);

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            try
            {
                _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

                _logger.LogConnectionConnect(
                    ServerAddress,
                    _information.LocalNetworkAddress,
                    _information.RemoteNetworkAddress);

                return _information;
            }
            catch (Exception exception)
            {
                _logger.LogConnectionConnectException(exception, ServerAddress);
                throw;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformDisposeAsync() : _decoratee.DisposeAsync();

        async ValueTask PerformDisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            _logger.LogConnectionDispose(
                ServerAddress,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _decoratee.InvokeAsync(request, cancel);

    public void OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    public void OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

    public Task ShutdownAsync(string message, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformShutdownAsync() : _decoratee.ShutdownAsync(message, cancel);

        async Task PerformShutdownAsync()
        {
            try
            {
                await _decoratee.ShutdownAsync(message, cancel).ConfigureAwait(false);

                _logger.LogConnectionShutdown(
                    ServerAddress,
                    _information.LocalNetworkAddress,
                    _information.RemoteNetworkAddress,
                    message);
            }
            catch (Exception exception)
            {
                _logger.LogConnectionShutdownException(
                    exception,
                    ServerAddress,
                    _information.LocalNetworkAddress,
                    _information.RemoteNetworkAddress);
                throw;
            }
        }
    }

    internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
