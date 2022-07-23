// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace IceRpc.Logger;

/// <summary>An interceptor that logs invocations using a logger with the "IceRpc.Logger" category. When used in
/// conjunction with the telemetry interceptor, install the logger interceptor after the telemetry interceptor; this
/// way, the logger include the scopes created by the telemetry activities.</summary>
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
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInvoke(request, response, stopwatch.Elapsed.TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogInvokeException(request, stopwatch.Elapsed.TotalMilliseconds, ex);
            throw;
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging logger interceptor messages.</summary>
internal static partial class LoggerInterceptorLoggerExtensions
{
    internal static void LogInvoke(
        this ILogger logger,
        OutgoingRequest request,
        IncomingResponse response,
        double totalMilliseconds)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInvoke(
                request.ServiceAddress,
                request.Operation,
                response.ResultType,
                response.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                response.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress,
                totalMilliseconds);
        }
    }

    internal static void LogInvokeException(
        this ILogger logger,
        OutgoingRequest request,
        double totalMilliseconds,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInvokeException(
                request.ServiceAddress,
                request.Operation,
                totalMilliseconds,
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.Invoke,
        EventName = nameof(LoggerInterceptorEventIds.Invoke),
        Level = LogLevel.Information,
        Message = "sent {Operation} to {ServiceAddress} using {LocalNetworkAddress}->{RemoteNetworkAddress} and " +
            "received {ResultType} response after {TotalMilliseconds:F} ms")]
    private static partial void LogInvoke(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        ResultType resultType,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        double totalMilliseconds);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.InvokeException,
        EventName = nameof(LoggerInterceptorEventIds.InvokeException),
        Level = LogLevel.Information,
        Message = "failed to send {Operation} to {ServiceAddress} in {TotalMilliseconds:F} ms")]
    private static partial void LogInvokeException(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        double totalMilliseconds,
        Exception exception);
}
