// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="ITaskExceptionObserver" /> using a <see cref="ILogger" />.</summary>
internal class LogTaskExceptionObserver : ITaskExceptionObserver
{
    private readonly ILogger _logger;

    public void DispatchFailed(
        IncomingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception) =>
        _logger.LogDispatchFailed(
            GetLogLevel(exception),
            request.Operation,
            request.Path,
            connectionInformation.RemoteNetworkAddress,
            exception);

    public void DispatchRefused(TransportConnectionInformation connectionInformation, Exception exception) =>
        _logger.LogDispatchRefused(GetLogLevel(exception), connectionInformation.RemoteNetworkAddress, exception);

    public void RequestPayloadContinuationFailed(
        OutgoingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception) =>
        _logger.LogRequestPayloadContinuationFailed(
            GetLogLevel(exception),
            request.Operation,
            request.ServiceAddress.Path,
            connectionInformation.RemoteNetworkAddress,
            exception);

    internal LogTaskExceptionObserver(ILogger logger) => _logger = logger;

    private static LogLevel GetLogLevel(Exception exception) =>
        exception switch
        {
            // expected during shutdown for example
            OperationCanceledException => LogLevel.Trace,

            // expected and somewhat common (peer aborts connection)
            IceRpcException rpcException when rpcException.IceRpcError is IceRpcError.ConnectionAborted =>
                LogLevel.Debug,

            // rare, for example a protocol error
            IceRpcException => LogLevel.Debug,

            // unexpected: from the application code (like a payload read exception) or bug in IceRpc
            _ => LogLevel.Warning
        };
}
