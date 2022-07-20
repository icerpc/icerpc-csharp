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
            _logger.LogInvoke(request, response, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogInvokeException(request, stopwatch.ElapsedMilliseconds, ex);
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
        long latency)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInvoke(
                request.ServiceAddress,
                request.Operation,
                request.IsOneway,
                response.ResultType,
                response.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                response.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress,
                latency);
        }
    }

    internal static void LogInvokeException(
        this ILogger logger,
        OutgoingRequest request,
        long latency,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInvokeException(
                request.ServiceAddress,
                request.Operation,
                request.IsOneway,
                latency,
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.Invoke,
        EventName = nameof(LoggerInterceptorEventIds.Invoke),
        Level = LogLevel.Information,
        Message = "sent request and received response {{ ServiceAddress = {ServiceAddress}, Operation = {Operation}, " +
            "IsOneway = {IsOneway}, ResultType = {ResultType}, LocalNetworkAddress = {LocalNetworkAddress}, " +
            "RemoteNetworkAddress = {RemoteNetworkAddress}, Latency = {Latency} ms }}")]
    private static partial void LogInvoke(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        bool isOneway,
        ResultType resultType,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        long latency);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.InvokeException,
        EventName = nameof(LoggerInterceptorEventIds.InvokeException),
        Level = LogLevel.Information,
        Message = "failed to send request {{ ServiceAddress = {ServiceAddress}, Operation = {Operation}, " +
            "IsOneway = {IsOneway}, Latency = {Latency} ms }}")]
    private static partial void LogInvokeException(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        bool isOneway,
        long latency,
        Exception exception);
}
