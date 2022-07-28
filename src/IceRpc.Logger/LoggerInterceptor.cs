// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
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
        _logger = loggerFactory.CreateLogger(GetType().FullName!);
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            _logger.LogInvoke(
                request.ServiceAddress,
                request.Operation,
                response.ResultType,
                response.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                response.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress);
            return response;
        }
        catch (Exception exception)
        {
            _logger.LogInvokeException(exception, request.ServiceAddress, request.Operation);
            throw;
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging logger interceptor messages.</summary>
internal static partial class LoggerInterceptorLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventId.Invoke,
        EventName = nameof(LoggerInterceptorEventId.Invoke),
        Level = LogLevel.Information,
        Message = "Sent {Operation} to {ServiceAddress} over {LocalNetworkAddress}<->{RemoteNetworkAddress} and "
            + "received {ResultType} response")]
    internal static partial void LogInvoke(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        ResultType resultType,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventId.InvokeException,
        EventName = nameof(LoggerInterceptorEventId.InvokeException),
        Level = LogLevel.Information,
        Message = "Failed to send {Operation} to {ServiceAddress}")]
    internal static partial void LogInvokeException(
        this ILogger logger,
        Exception exception,
        ServiceAddress serviceAddress,
        string operation);
}
