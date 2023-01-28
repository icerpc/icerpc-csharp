// Copyright (c) ZeroC, Inc. All rights reserved.

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
        Exception exception)
    {
        LogLevel logLevel = exception is IceRpcException or OperationCanceledException ?
            LogLevel.Debug : // expected
            LogLevel.Warning; // not expected

        _logger.LogDispatchFailed(
            logLevel,
            request.Operation,
            request.Path,
            connectionInformation.RemoteNetworkAddress,
            exception);
    }

    public void DispatchRefused(TransportConnectionInformation connectionInformation, Exception exception)
    {
        LogLevel logLevel = exception is IceRpcException or OperationCanceledException ?
            LogLevel.Debug : // expected
            LogLevel.Warning; // not expected

        _logger.LogDispatchRefused(logLevel, connectionInformation.RemoteNetworkAddress, exception);
    }

    public void RequestPayloadContinuationFailed(
        OutgoingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception)
    {
        LogLevel logLevel = exception is IceRpcException or OperationCanceledException ?
            LogLevel.Debug : // expected
            LogLevel.Warning; // not expected

        _logger.LogRequestPayloadContinuationFailed(
            logLevel,
            request.Operation,
            request.ServiceAddress.Path,
            connectionInformation.RemoteNetworkAddress,
            exception);
    }

    internal LogTaskExceptionObserver(ILogger logger) => _logger = logger;
}
