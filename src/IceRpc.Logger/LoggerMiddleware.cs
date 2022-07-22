// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
    {
        _logger.LogReceivedRequest(
            request.ConnectionContext.TransportConnectionInformation,
            request.Path,
            request.Operation);

        var stopwatch = new Stopwatch();
        stopwatch.Start();
        try
        {
            OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
            stopwatch.Stop();
            if (!request.IsOneway)
            {
                _logger.LogSendingResponse(
                    request.ConnectionContext.TransportConnectionInformation,
                    request.Path,
                    request.Operation,
                    response.ResultType,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogDispatchException(
                request.ConnectionContext.TransportConnectionInformation,
                request.Path,
                request.Operation,
                stopwatch.Elapsed.TotalMilliseconds,
                ex);
            throw;
        }
    }
}

internal static partial class LoggerMiddlewareLoggerExtensions
{
    internal static void LogDispatchException(
        this ILogger logger,
        TransportConnectionInformation transportConnectionInformation,
        string path,
        string operation,
        double totalMilliseconds,
        Exception ex)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogDispatchException(
                transportConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                transportConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation,
                totalMilliseconds,
                ex);
        }
    }

    internal static void LogReceivedRequest(
        this ILogger logger,
        TransportConnectionInformation transportConnectionInformation,
        string path,
        string operation)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogReceivedRequest(
                transportConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                transportConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation);
        }
    }

    internal static void LogSendingResponse(
        this ILogger logger,
        TransportConnectionInformation transportConnectionInformation,
        string path,
        string operation,
        ResultType resultType,
        double totalMilliseconds)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogSendingResponse(
                transportConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                transportConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation,
                resultType,
                totalMilliseconds);
        }
    }

    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventIds.DispatchException,
        EventName = nameof(LoggerMiddlewareEventIds.DispatchException),
        Level = LogLevel.Information,
        Message = "failed to dispatch {Operation} to {Path} using {LocalNetworkAddress} -> {RemoteNetworkAddress} in " +
                  "{TotalMilliseconds:F} ms")]
    private static partial void LogDispatchException(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation,
        double totalMilliseconds,
        Exception ex);

    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventIds.ReceivedRequest,
        EventName = nameof(LoggerMiddlewareEventIds.ReceivedRequest),
        Level = LogLevel.Information,
        Message = "received {Operation} to {Path} using {LocalNetworkAddress} -> {RemoteNetworkAddress}")]
    private static partial void LogReceivedRequest(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation);

    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventIds.SendingResponse,
        EventName = nameof(LoggerMiddlewareEventIds.SendingResponse),
        Level = LogLevel.Information,
        Message = "sending {Operation} to {Path} using {LocalNetworkAddress} -> {RemoteNetworkAddress} and " +
            "received {ResultType} in {TotalMilliseconds:F} ms")]
    private static partial void LogSendingResponse(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation,
        ResultType resultType,
        double totalMilliseconds);
}
