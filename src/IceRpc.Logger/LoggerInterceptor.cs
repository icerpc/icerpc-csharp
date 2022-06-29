// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Logger;

/// <summary>An interceptor that logs request and response messages using a logger with the "IceRpc" category. When
/// used in conjunction with the telemetry interceptor, install the logger interceptor after the telemetry interceptor,
/// this way the logger can include the scopes created by the telemetry activities.</summary>
public class LoggerInterceptor : IInvoker
{
    private readonly ILogger _logger;
    private readonly IInvoker _next;

    /// <summary>Constructs a logger interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="loggerFactory">The logger factory used to create the logger.</param>
    public LoggerInterceptor(IInvoker next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger("IceRpc.Logger");
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        try
        {
            _logger.LogSendingRequest(request.Proxy.Path, request.Operation);
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            if (!request.IsOneway)
            {
                _logger.LogReceivedResponse(
                    request.Features.Get<INetworkConnectionInformationFeature>(),
                    request.Proxy.Path,
                    request.Operation,
                    response.ResultType);
            }
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogInvokeException(
                request.Features.Get<INetworkConnectionInformationFeature>(),
                request.Proxy.Path,
                request.Operation,
                ex);
            throw;
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging logger interceptor messages.</summary>
internal static partial class LoggerInterceptorLoggerExtensions
{
    internal static void LogInvokeException(
        this ILogger logger,
        INetworkConnectionInformationFeature? feature,
        string path,
        string operation,
        Exception ex)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInvokeException(
                feature?.LocalEndPoint.ToString() ?? "undefined",
                feature?.RemoteEndPoint.ToString() ?? "undefined",
                path,
                operation,
                ex);
        }
    }

    internal static void LogReceivedResponse(
        this ILogger logger,
        INetworkConnectionInformationFeature? feature,
        string path,
        string operation,
        ResultType resultType)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogReceivedResponse(
                feature?.LocalEndPoint.ToString() ?? "undefined",
                feature?.RemoteEndPoint.ToString() ?? "undefined",
                path,
                operation,
                resultType);
        }
    }

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.SendingRequest,
        EventName = nameof(LoggerInterceptorEventIds.SendingRequest),
        Level = LogLevel.Information,
        Message = "sending request (Path={Path}, Operation={Operation})")]
    internal static partial void LogSendingRequest(this ILogger logger, string path, string operation);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.InvokeException,
        EventName = nameof(LoggerInterceptorEventIds.InvokeException),
        Level = LogLevel.Information,
        Message = "request invocation exception (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                  "Path={Path}, Operation={Operation})")]
    private static partial void LogInvokeException(
        this ILogger logger,
        string localEndpoint,
        string remoteEndpoint,
        string path,
        string operation,
        Exception ex);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.ReceivedResponse,
        EventName = nameof(LoggerInterceptorEventIds.ReceivedResponse),
        Level = LogLevel.Information,
        Message = "received response (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                  "Path={Path}, Operation={Operation}, ResultType={ResultType})")]
    private static partial void LogReceivedResponse(
        this ILogger logger,
        string localEndpoint,
        string remoteEndpoint,
        string path,
        string operation,
        ResultType resultType);
}
