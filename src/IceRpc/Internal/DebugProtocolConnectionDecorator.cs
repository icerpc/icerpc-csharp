// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>A debug decorator for protocol connections.</summary>
internal class DebugProtocolConnectionDecorator : IProtocolConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private IConnectionContext? _connectionContext;
    private readonly IProtocolConnection _decoratee;
    private readonly ILogger _logger;

    async Task<TransportConnectionInformation> IProtocolConnection.ConnectAsync(CancellationToken cancel)
    {
        TransportConnectionInformation information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
        _connectionContext ??= new ConnectionContext(this, information);
        return information;
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _decoratee.DisposeAsync();

    async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            IncomingResponse response = await _decoratee.InvokeAsync(request, cancel).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogProtocolConnectionInvoke(
                request.ServiceAddress,
                request.Operation,
                request.IsOneway,
                response.ResultType,
                response.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                response.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress,
                stopwatch.Elapsed.TotalMilliseconds);

            // Since the log InvokeAsync, we need to replace the connection context with 'this' as the invoker.
            // See also the DebugDispatcherDecorator.
            response.ConnectionContext = _connectionContext!;
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogProtocolConnectionInvokeException(
                request.ServiceAddress,
                request.Operation,
                request.IsOneway,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);
            throw;
        }
    }

    void IProtocolConnection.OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    void IProtocolConnection.OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

    Task IProtocolConnection.ShutdownAsync(string message, CancellationToken cancel) =>
        _decoratee.ShutdownAsync(message, cancel);

    internal DebugProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
