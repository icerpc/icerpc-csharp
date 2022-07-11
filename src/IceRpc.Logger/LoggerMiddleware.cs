// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

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
        _logger.LogReceivedRequest(request.NetworkConnectionInformation, request.Path, request.Operation);
        try
        {
            OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
            if (!request.IsOneway)
            {
                _logger.LogSendingResponse(
                    request.NetworkConnectionInformation,
                    request.Path,
                    request.Operation,
                    response.ResultType);
            }
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogDispatchException(request.NetworkConnectionInformation, request.Path, request.Operation, ex);
            throw;
        }
    }
}

internal static partial class LoggerMiddlewareLoggerExtensions
{
    internal static void LogDispatchException(
        this ILogger logger,
        NetworkConnectionInformation networkConnectionInformation,
        string path,
        string operation,
        Exception ex)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogDispatchException(
                networkConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                networkConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation,
                ex);
        }
    }

    internal static void LogReceivedRequest(
        this ILogger logger,
        NetworkConnectionInformation networkConnectionInformation,
        string path,
        string operation)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogReceivedRequest(
                networkConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                networkConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation);
        }
    }

    internal static void LogSendingResponse(
        this ILogger logger,
        NetworkConnectionInformation networkConnectionInformation,
        string path,
        string operation,
        ResultType resultType)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogSendingResponse(
                networkConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                networkConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation,
                resultType);
        }
    }

    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventIds.DispatchException,
        EventName = nameof(LoggerMiddlewareEventIds.DispatchException),
        Level = LogLevel.Information,
        Message = "request dispatch exception (LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress}, " +
                  "Path={Path}, Operation={Operation})")]
    private static partial void LogDispatchException(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation,
        Exception ex);

    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventIds.ReceivedRequest,
        EventName = nameof(LoggerMiddlewareEventIds.ReceivedRequest),
        Level = LogLevel.Information,
        Message = "received request (LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress}, " +
                  "Path={Path}, Operation={Operation})")]
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
        Message = "sending response (LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress}, " +
                  "Path={Path}, Operation={Operation}, ResultType={ResultType})")]
    private static partial void LogSendingResponse(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation,
        ResultType resultType);
}
