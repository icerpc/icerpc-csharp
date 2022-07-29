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

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformConnectAsync() : _decoratee.ConnectAsync(cancel);

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            try
            {
                _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

                _logger.LogConnectionConnect(
                    Endpoint,
                    _information.LocalNetworkAddress,
                    _information.RemoteNetworkAddress);

                return _information;
            }
            catch (Exception exception)
            {
                _logger.LogConnectionConnectException(exception, Endpoint);
                throw;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformDisposeAsync() : _decoratee.DisposeAsync();

        async ValueTask PerformDisposeAsync()
        {
            using IDisposable _ = _logger.StartConnectionShutdownScope(_information);
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            _logger.LogConnectionDispose(Endpoint);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformInvokeAsync() : _decoratee.InvokeAsync(request, cancel);

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            using IDisposable _ = _logger.StartConnectionInvocationScope(request);

            try
            {
                IncomingResponse response = await _decoratee.InvokeAsync(request, cancel).ConfigureAwait(false);
                _logger.LogConnectionInvoke(
                    response.ResultType,
                    _information.LocalNetworkAddress,
                    _information.RemoteNetworkAddress);

                return response;
            }
            catch (Exception exception)
            {
                _logger.LogConnectionInvokeException(exception);
                throw;
            }
        }
    }

    public void OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    public void OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

    public Task ShutdownAsync(string message, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformShutdownAsync() : _decoratee.ShutdownAsync(message, cancel);

        async Task PerformShutdownAsync()
        {
            using IDisposable _ = _logger.StartConnectionShutdownScope(_information);

            try
            {
                await _decoratee.ShutdownAsync(message, cancel).ConfigureAwait(false);

                _logger.LogConnectionShutdown(Endpoint, message);
            }
            catch (Exception exception)
            {
                _logger.LogConnectionShutdownException(exception, Endpoint);
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
