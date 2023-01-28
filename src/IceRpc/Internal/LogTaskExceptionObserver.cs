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
            OperationCanceledException => LogLevel.Debug, // expected
            IceRpcException rpcException when rpcException.IceRpcError != IceRpcError.IceRpcError => LogLevel.Debug,
            _ => LogLevel.Warning
        };
    }
