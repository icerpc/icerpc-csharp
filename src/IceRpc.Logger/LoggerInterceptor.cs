// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
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
        _logger.LogSendingRequest(request.ServiceAddress.Path, request.Operation);
        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            if (!request.IsOneway)
            {
                _logger.LogReceivedResponse(
                    response.ConnectionContext,
                    request.ServiceAddress.Path,
                    request.Operation,
                    response.ResultType);
            }
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogInvokeException(request.ServiceAddress.Path, request.Operation, ex);
            throw;
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging logger interceptor messages.</summary>
internal static partial class LoggerInterceptorLoggerExtensions
{
    [LoggerMessage(
       EventId = (int)LoggerInterceptorEventIds.InvokeException,
       EventName = nameof(LoggerInterceptorEventIds.InvokeException),
       Level = LogLevel.Information,
       Message = "request invocation exception (Path={Path}, Operation={Operation})")]
    internal static partial void LogInvokeException(
       this ILogger logger,
       string path,
       string operation,
       Exception ex);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.SendingRequest,
        EventName = nameof(LoggerInterceptorEventIds.SendingRequest),
        Level = LogLevel.Information,
        Message = "sending request (Path={Path}, Operation={Operation})")]
    internal static partial void LogSendingRequest(this ILogger logger, string path, string operation);

    internal static void LogReceivedResponse(
        this ILogger logger,
        IConnectionContext connectionContext,
        string path,
        string operation,
        ResultType resultType)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogReceivedResponse(
                connectionContext.NetworkConnectionInformation.LocalNetworkAddress?.ToString() ?? "undefined",
                connectionContext.NetworkConnectionInformation.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation,
                resultType);
        }
    }

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.ReceivedResponse,
        EventName = nameof(LoggerInterceptorEventIds.ReceivedResponse),
        Level = LogLevel.Information,
        Message = "received response (LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress=" +
            "{RemoteNetworkAddress}, Path={Path}, Operation={Operation}, ResultType={ResultType})")]
    private static partial void LogReceivedResponse(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation,
        ResultType resultType);
}
