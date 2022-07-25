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
            _logger.LogInvoke(
                request.ServiceAddress,
                request.Operation,
                request.IsOneway,
                response.ResultType,
                response.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                response.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress,
                stopwatch.Elapsed.TotalMilliseconds,
                Environment.NewLine);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogInvokeException(
                exception,
                request.ServiceAddress,
                request.Operation,
                request.IsOneway,
                stopwatch.Elapsed.TotalMilliseconds,
                Environment.NewLine);
            throw;
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging logger interceptor messages.</summary>
internal static partial class LoggerInterceptorLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.Invoke,
        EventName = nameof(LoggerInterceptorEventIds.Invoke),
        Level = LogLevel.Information,
        Message = "Sent request and received response:{NewLine}" +
        "ServiceAddress: {ServiceAddress}{NewLine}" +
        "Operation: {Operation}{NewLine}" +
        "IsOneway: {IsOneway}{NewLine}" +
        "LocalNetworkAddress: {LocalNetworkAddress}{NewLine}" +
        "RemoteNetworkAddress: {RemoteNetworkAddress}{NewLine}" +
        "ResultType: {ResultType}{NewLine}" +
        "Time: {TotalMilliseconds:F} ms")]
    internal static partial void LogInvoke(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        bool isOneway,
        ResultType resultType,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        double totalMilliseconds,
        string newLine);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventIds.InvokeException,
        EventName = nameof(LoggerInterceptorEventIds.InvokeException),
        Level = LogLevel.Information,
        Message = "Failed to send request:{NewLine}" +
        "ServiceAddress: {ServiceAddress}{NewLine}" +
        "Operation: {Operation}{NewLine}" +
        "IsOneway: {IsOneway}{NewLine}" +
        "Time: {TotalMilliseconds:F} ms")]
    internal static partial void LogInvokeException(
        this ILogger logger,
        Exception exception,
        ServiceAddress serviceAddress,
        string operation,
        bool isOneway,
        double totalMilliseconds,
        string newLine);
}
