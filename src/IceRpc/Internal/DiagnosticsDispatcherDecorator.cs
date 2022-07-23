// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension for dispatchers.</summary>
internal static partial class DispatcherLoggerExtensions
{
    private static readonly Func<ILogger, string, string, IDisposable> _dispatchScope =
        LoggerMessage.DefineScope<string, string>("Dispatch {{ Path = {Path}, Operation = {Operation} }}");

    [LoggerMessage(
        EventId = (int)DispatcherDiagnosticsEventIds.Dispatch,
        EventName = nameof(DispatcherDiagnosticsEventIds.Dispatch),
        Level = LogLevel.Debug,
        Message = "dispatch completed {{ IsOneway = {IsOneway}, ResultType = {ResultType}, " +
            "Time = {TotalMilliseconds} }}")]
    internal static partial void LogDispatch(
        this ILogger logger,
        bool isOneway,
        ResultType resultType,
        double totalMilliseconds);

    [LoggerMessage(
       EventId = (int)DispatcherDiagnosticsEventIds.DispatchException,
       EventName = nameof(DispatcherDiagnosticsEventIds.DispatchException),
       Level = LogLevel.Debug,
       Message = "dispatch exception {{ IsOneway = {IsOneway}, Time = {TotalMilliseconds} }}")]
    internal static partial void LogDispatchException(
       this ILogger logger,
       bool isOneway,
       double totalMilliseconds,
       Exception exception);

    internal static IDisposable StartDispatchScope(this ILogger logger, string path, string operation) =>
        _dispatchScope(logger, path, operation);

    private enum DispatcherDiagnosticsEventIds
    {
        Dispatch = BaseEventIds.Dispatcher + (BaseEventIds.EventIdRange / 2),
        DispatchException
    }
}

internal class DiagnosticsDispatcherDecorator : IDispatcher
{
    private readonly IDispatcher _decoratee;
    private readonly ILogger _logger;

    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
    {
        using IDisposable _ = _logger.StartDispatchScope(request.Path, request.Operation);

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            OutgoingResponse response = await _decoratee.DispatchAsync(request, cancel).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogDispatch(
                request.IsOneway,
                response.ResultType,
                stopwatch.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogDispatchException(
                request.IsOneway,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);
            throw;
        }
    }

    internal DiagnosticsDispatcherDecorator(IDispatcher decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
