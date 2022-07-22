// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension for dispatchers.</summary>
internal static partial class DispatcherLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)DispatcherDebugEventIds.Dispatch,
        EventName = nameof(DispatcherDebugEventIds.Dispatch),
        Level = LogLevel.Debug,
        Message = "dispatch completed {{ Path = {Path}, Operation = {Operation}, IsOneway = {IsOneway}, " +
            "ResultType = {ResultType}, Time = {TotalMilliseconds} }}")]
    internal static partial void LogDispatch(
        this ILogger logger,
        string path,
        string operation,
        bool isOneway,
        ResultType resultType,
        double totalMilliseconds);

    [LoggerMessage(
       EventId = (int)DispatcherDebugEventIds.DispatchException,
       EventName = nameof(DispatcherDebugEventIds.DispatchException),
       Level = LogLevel.Debug,
       Message = "dispatch exception {{ Path = {Path}, Operation = {Operation}, IsOneway = {IsOneway}, " +
           "Time = {TotalMilliseconds} }}")]
    internal static partial void LogDispatchException(
       this ILogger logger,
       string path,
       string operation,
       bool isOneway,
       double totalMilliseconds,
       Exception exception);

    private enum DispatcherDebugEventIds
    {
        Dispatch = BaseEventIds.Dispatcher + (BaseEventIds.EventIdRange / 2),
        DispatchException
    }
}

/// <summary>A debug decorator for dispatcher.</summary>
internal class DebugDispatcherDecorator : IDispatcher
{
    private IConnectionContext? _connectionContext;
    private readonly IDispatcher _decoratee;
    private readonly ILogger _logger;
    private IProtocolConnection? _protocolConnection;

    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
    {
        if (_connectionContext is null)
        {
            Debug.Assert(_protocolConnection is not null);
            _connectionContext = new ConnectionContext(
                _protocolConnection,
                request.ConnectionContext.TransportConnectionInformation);
        }

        request.ConnectionContext = _connectionContext;

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            OutgoingResponse response = await _decoratee.DispatchAsync(request, cancel).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogDispatch(
                request.Path,
                request.Operation,
                request.IsOneway,
                response.ResultType,
                stopwatch.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogDispatchException(
                request.Path,
                request.Operation,
                request.IsOneway,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);
            throw;
        }
    }

    internal DebugDispatcherDecorator(IDispatcher decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }

    internal void SetProtocolConnection(IProtocolConnection protocolConnection) =>
        _protocolConnection = protocolConnection;
}
