// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace IceRpc.Logger;

/// <summary>A middleware that logs request and response messages using a logger with the "IceRpc" category. When
/// used in conjunction with the telemetry middleware, install the logger middleware after the telemetry middleware,
/// this way the logger can include the scopes created by the telemetry activities.</summary>
public class LoggerMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly ILogger _logger;

    /// <summary>Constructs a logger middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="loggerFactory">The logger factory used to create the logger.</param>
    public LoggerMiddleware(IDispatcher next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger("IceRpc.Logger");
    }

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Information) ? PerformDispatchAsync() : _next.DispatchAsync(request, cancel);

        async ValueTask<OutgoingResponse> PerformDispatchAsync()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
                stopwatch.Stop();

                _logger.LogDispatch(
                    request.Path,
                    request.Operation,
                    request.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                    request.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress,
                    response.ResultType,
                    stopwatch.Elapsed.TotalMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogDispatchException(
                    ex,
                    request.Path,
                    request.Operation,
                    stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging LoggerMiddleware messages.</summary>
internal static partial class LoggerMiddlewareLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.Invoke,
        EventName = nameof(LoggerInterceptorEventIds.Invoke),
        Level = LogLevel.Information,
        Message = "Dispatched {Operation} to {Path} using {LocalNetworkAddress}<->{RemoteNetworkAddress} and " +
            "received {ResultType} response after {TotalMilliseconds:F} ms")]
    internal static partial void LogDispatch(
        this ILogger logger,
        string path,
        string operation,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        ResultType resultType,
        double totalMilliseconds);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.InvokeException,
        EventName = nameof(LoggerInterceptorEventIds.InvokeException),
        Level = LogLevel.Information,
        Message = "Failed to dispatch {Operation} to {Path} in {TotalMilliseconds:F} ms")]
    internal static partial void LogDispatchException(
        this ILogger logger,
        Exception exception,
        string path,
        string operation,
        double totalMilliseconds);
}
