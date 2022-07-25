// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for connection event IDs.</summary>
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

    private static readonly Func<ILogger, Endpoint, EndPoint?, EndPoint?, IDisposable> _shutdownScope =
        LoggerMessage.DefineScope<Endpoint, EndPoint?, EndPoint?>(
            "Endpoint:{Endpoint}, LocalNetworkAddress:{LocalNetworkAddress}, " +
            "RemoteNetworkAddress:{RemoteNetworkAddress}");

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Connect,
        EventName = nameof(ConnectionEventIds.Connect),
        Level = LogLevel.Debug,
        Message =
            "Established connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogConnectionConnect(
        this ILogger logger,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectException,
        EventName = nameof(ConnectionEventIds.ConnectException),
        Level = LogLevel.Debug,
        Message = "Failed to establish connection for {Endpoint}")]
    internal static partial void LogConnectionConnectException(
        this ILogger logger,
        Exception exception,
        Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Dispatch,
        EventName = nameof(ConnectionEventIds.Dispatch),
        Level = LogLevel.Debug,
        Message = "Returning {ResultType} response")]
    internal static partial void LogConnectionDispatch(this ILogger logger, ResultType resultType);

    [LoggerMessage(
       EventId = (int)ConnectionEventIds.DispatchException,
       EventName = nameof(ConnectionEventIds.DispatchException),
       Level = LogLevel.Debug,
       Message = "Failed to dispatch request")]
    internal static partial void LogConnectionDispatchException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Dispose,
        EventName = nameof(ConnectionEventIds.Dispose),
        Level = LogLevel.Debug,
        Message = "Disposed {Protocol} connection")]
    internal static partial void LogConnectionDispose(this ILogger logger, Protocol protocol);

    [LoggerMessage(
       EventId = (int)ConnectionEventIds.Invoke,
       EventName = nameof(ConnectionEventIds.Invoke),
       Level = LogLevel.Debug,
       Message = "Sent request and received {ResultType} response over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogConnectionInvoke(
       this ILogger logger,
       ResultType resultType,
       EndPoint? localNetworkAddress,
       EndPoint? remoteNetworkAddress);

    [LoggerMessage(
       EventId = (int)ConnectionEventIds.InvokeException,
       EventName = nameof(ConnectionEventIds.InvokeException),
       Level = LogLevel.Debug,
       Message = "Failed to send request")]
    internal static partial void LogConnectionInvokeException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Shutdown,
        EventName = nameof(ConnectionEventIds.Shutdown),
        Level = LogLevel.Debug,
        Message = "Shut down {Protocol} connection: {Message}")]
    internal static partial void LogConnectionShutdown(
        this ILogger logger,
        Protocol protocol,
        string message);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ShutdownException,
        EventName = nameof(ConnectionEventIds.ShutdownException),
        Level = LogLevel.Debug,
        Message = "Failed to shut down {Protocol} connection")]
    internal static partial void LogConnectionShutdownException(
        this ILogger logger,
        Exception exception,
        Protocol protocol);

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
        Endpoint endpoint,
        TransportConnectionInformation information) =>
        _shutdownScope(logger, endpoint, information.LocalNetworkAddress, information.RemoteNetworkAddress);
}
