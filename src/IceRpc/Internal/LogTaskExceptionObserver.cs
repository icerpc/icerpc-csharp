// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="ITaskExceptionObserver" /> using a <see cref="ILogger" />.</summary>
internal class LogTaskExceptionObserver : ITaskExceptionObserver
{
    private readonly ILogger _logger;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "GetLogLevel is a trivial switch on the exception type and is required to categorize the log.")]
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "GetLogLevel is a trivial switch on the exception type and is required to categorize the log.")]
    public void DispatchRefused(TransportConnectionInformation connectionInformation, Exception exception) =>
        _logger.LogDispatchRefused(GetLogLevel(exception), connectionInformation.RemoteNetworkAddress, exception);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "GetLogLevel is a trivial switch on the exception type and is required to categorize the log.")]
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

            // usually expected, e.g. peer aborts connection
            IceRpcException => LogLevel.Debug,

            // unexpected: from the application code (like a payload read exception) or bug in IceRpc
            _ => LogLevel.Warning
        };
}
