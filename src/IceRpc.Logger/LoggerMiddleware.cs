// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Logger;

/// <summary>A middleware that writes a log entry to an <see cref="ILogger" /> for each dispatch.</summary>
public class LoggerMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly ILogger _logger;

    /// <summary>Constructs a logger middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="logger">The logger to log to.</param>
    public LoggerMiddleware(IDispatcher next, ILogger logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        return _logger.IsEnabled(LogLevel.Information) ?
            PerformDispatchAsync() :
            _next.DispatchAsync(request, cancellationToken);

        async ValueTask<OutgoingResponse> PerformDispatchAsync()
        {
            try
            {
                OutgoingResponse response = await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);

                _logger.LogDispatch(
                    request.Path,
                    request.Operation,
                    request.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
                    request.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress,
                    response.StatusCode);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogDispatchException(
                    ex,
                    request.Path,
                    request.Operation);
                throw;
            }
        }
    }
}

/// <summary>This class contains the ILogger extension methods for logging LoggerMiddleware messages.</summary>
internal static partial class LoggerMiddlewareLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventId.Dispatch,
        EventName = nameof(LoggerMiddlewareEventId.Dispatch),
        Level = LogLevel.Information,
        Message = "Dispatch of {Operation} to {Path} over {LocalNetworkAddress}<->{RemoteNetworkAddress} returned a {StatusCode} response")]
    internal static partial void LogDispatch(
        this ILogger logger,
        string path,
        string operation,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        StatusCode statusCode);

    [LoggerMessage(
        EventId = (int)LoggerMiddlewareEventId.DispatchException,
        EventName = nameof(LoggerMiddlewareEventId.DispatchException),
        Level = LogLevel.Information,
        Message = "Failed to dispatch {Operation} to {Path}")]
    internal static partial void LogDispatchException(
        this ILogger logger,
        Exception exception,
        string path,
        string operation);
}
