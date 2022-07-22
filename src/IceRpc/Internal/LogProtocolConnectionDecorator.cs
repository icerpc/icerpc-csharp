// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogProtocolConnectionConnect(
                   Endpoint.Protocol,
                   Endpoint,
                   _information.LocalNetworkAddress,
                   _information.RemoteNetworkAddress,
                   stopwatch.Elapsed.TotalMilliseconds);

            return _information;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogProtocolConnectionConnectException(
                Endpoint.Protocol,
                Endpoint,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);
            throw;
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        await _decoratee.DisposeAsync().ConfigureAwait(false);
        _logger.LogProtocolConnectionDispose(
            Endpoint.Protocol,
            Endpoint,
            _information.LocalNetworkAddress,
            _information.RemoteNetworkAddress,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    // Since we don't log InvokeAsync, we don't need to replace the connection context.
    Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _decoratee.InvokeAsync(request, cancel);

    void IProtocolConnection.OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    void IProtocolConnection.OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

    async Task IProtocolConnection.ShutdownAsync(string message, CancellationToken cancel)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            await _decoratee.ShutdownAsync(message, cancel).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogProtocolConnectionShutdown(
                Endpoint.Protocol,
                Endpoint,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress,
                stopwatch.Elapsed.TotalMilliseconds,
                message);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogProtocolConnectionShutdownException(
                Endpoint.Protocol,
                Endpoint,
                _information.LocalNetworkAddress,
                _information.RemoteNetworkAddress,
                stopwatch.Elapsed.TotalMilliseconds,
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
