// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Logger;

/// <summary>An interceptor that writes a log entry to an <see cref="ILogger" /> for each invocation.</summary>
/// <seealso cref="LoggerPipelineExtensions"/>
/// <seealso cref="LoggerInvokerBuilderExtensions"/>
public class LoggerInterceptor : IInvoker
{
    private readonly ILogger _logger;
    private readonly IInvoker _next;

    /// <summary>Constructs a logger interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="logger">The logger to log to.</param>
    public LoggerInterceptor(IInvoker next, ILogger logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogInvoke(
                request.ServiceAddress,
                request.Operation,
                response.StatusCode,
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

/// <summary>Provides <see cref="ILogger" /> extension methods for logging <see cref="LoggerInterceptor" />
/// messages.</summary>
internal static partial class LoggerInterceptorLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventId.Invoke,
        EventName = nameof(LoggerInterceptorEventId.Invoke),
        Level = LogLevel.Information,
        Message = "Sent request {Operation} to {ServiceAddress} over {LocalNetworkAddress}<->{RemoteNetworkAddress} and received a response with status code {StatusCode}")]
    internal static partial void LogInvoke(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation,
        StatusCode statusCode,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)LoggerInterceptorEventId.InvokeException,
        EventName = nameof(LoggerInterceptorEventId.InvokeException),
        Level = LogLevel.Information,
        Message = "Failed to send request {Operation} to {ServiceAddress}")]
    internal static partial void LogInvokeException(
        this ILogger logger,
        Exception exception,
        ServiceAddress serviceAddress,
        string operation);
}
