// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for connection events.</summary>
internal static partial class ConnectionLoggerExtensions
{
    // We cannot create a "connect scope" because ConnectAsync creates long running tasks and we don't want these
    // tasks to inherit this scope.

    private static readonly Func<ILogger, string, string, EndPoint?, EndPoint?, IDisposable> _dispatchScope =
        LoggerMessage.DefineScope<string, string, EndPoint?, EndPoint?>(
            "Path:{Path}, Operation:{Operation}, LocalNetworkAddress:{LocalNetworkAddress}, " +
            "RemoteNetworkAddress:{RemoteNetworkAddress}");

    private static readonly Func<ILogger, ServiceAddress, string, IDisposable> _invocationScope =
        LoggerMessage.DefineScope<ServiceAddress, string>("ServiceAddress:{ServiceAddress}, Operation:{Operation}");

    private static readonly Func<ILogger, EndPoint?, EndPoint?, IDisposable> _shutdownScope =
        LoggerMessage.DefineScope<EndPoint?, EndPoint?>(
            "LocalNetworkAddress:{LocalNetworkAddress}, RemoteNetworkAddress:{RemoteNetworkAddress}");

    [LoggerMessage(
        EventId = (int)ConnectionEventId.Connect,
        EventName = nameof(ConnectionEventId.Connect),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} established over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogConnectionConnect(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.ConnectException,
        EventName = nameof(ConnectionEventId.ConnectException),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} could not be established")]
    internal static partial void LogConnectionConnectException(
        this ILogger logger,
        Exception exception,
        ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.Dispatch,
        EventName = nameof(ConnectionEventId.Dispatch),
        Level = LogLevel.Debug,
        Message = "Connection returning {ResultType} response")]
    internal static partial void LogConnectionDispatch(this ILogger logger, ResultType resultType);

    [LoggerMessage(
       EventId = (int)ConnectionEventId.DispatchException,
       EventName = nameof(ConnectionEventId.DispatchException),
       Level = LogLevel.Debug,
       Message = "Connection failed to dispatch request")]
    internal static partial void LogConnectionDispatchException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.Dispose,
        EventName = nameof(ConnectionEventId.Dispose),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} disposed")]
    internal static partial void LogConnectionDispose(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
       EventId = (int)ConnectionEventId.Invoke,
       EventName = nameof(ConnectionEventId.Invoke),
       Level = LogLevel.Debug,
       Message = "Connection sent request and received {ResultType} response over " +
        "{LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogConnectionInvoke(
       this ILogger logger,
       ResultType resultType,
       EndPoint? localNetworkAddress,
       EndPoint? remoteNetworkAddress);

    [LoggerMessage(
       EventId = (int)ConnectionEventId.InvokeException,
       EventName = nameof(ConnectionEventId.InvokeException),
       Level = LogLevel.Debug,
       Message = "Connection failed to send request")]
    internal static partial void LogConnectionInvokeException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.Shutdown,
        EventName = nameof(ConnectionEventId.Shutdown),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} shut down successfully: {Message}")]
    internal static partial void LogConnectionShutdown(
        this ILogger logger,
        ServerAddress serverAddress,
        string message);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.ShutdownException,
        EventName = nameof(ConnectionEventId.ShutdownException),
        Level = LogLevel.Debug,
        Message = "Connection {ServerAddress} failed to shut down")]
    internal static partial void LogConnectionShutdownException(
        this ILogger logger,
        Exception exception,
        ServerAddress serverAddress);

    internal static IDisposable StartConnectionDispatchScope(this ILogger logger, IncomingRequest request) =>
        _dispatchScope(
            logger,
            request.Path,
            request.Operation,
            request.ConnectionContext.TransportConnectionInformation.LocalNetworkAddress,
            request.ConnectionContext.TransportConnectionInformation.RemoteNetworkAddress);

    internal static IDisposable StartConnectionInvocationScope(this ILogger logger, OutgoingRequest request) =>
        _invocationScope(logger, request.ServiceAddress, request.Operation);

    internal static IDisposable StartConnectionShutdownScope(
        this ILogger logger,
        TransportConnectionInformation information) =>
        _shutdownScope(logger, information.LocalNetworkAddress, information.RemoteNetworkAddress);
}
