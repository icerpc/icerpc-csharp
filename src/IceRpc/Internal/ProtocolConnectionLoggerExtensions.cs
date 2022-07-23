// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for protocol connection APIs.</summary>
internal static partial class ProtocolConnectionLoggerExtensions
{
    private static readonly Func<ILogger, ServiceAddress, string, IDisposable> _invocationScope =
        LoggerMessage.DefineScope<ServiceAddress, string>(
            "Invocation {{ ServiceAddress = {ServiceAddress}, Operation = {Operation} }}");

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Connect,
        EventName = nameof(ConnectionEventIds.Connect),
        Level = LogLevel.Information,
        Message = "established {Protocol} connection for {Endpoint} over " +
            "{LocalNetworkAddress}<->{RemoteNetworkAddress} in {TotalMilliseconds:F} ms")]
    internal static partial void LogProtocolConnectionConnect(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        double totalMilliseconds);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectException,
        EventName = nameof(ConnectionEventIds.ConnectException),
        Level = LogLevel.Information,
        Message = "failed to establish {Protocol} connection for {Endpoint} after {TotalMilliseconds:F} ms")]
    internal static partial void LogProtocolConnectionConnectException(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        double totalMilliseconds,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Dispose,
        EventName = nameof(ConnectionEventIds.Dispose),
        Level = LogLevel.Information,
        Message = "disposed {Protocol} connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress} " +
            "in {TotalMilliseconds:F} ms")]
    internal static partial void LogProtocolConnectionDispose(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        double totalMilliseconds);

    [LoggerMessage(
       EventId = (int)ConnectionDiagnosticsEventIds.Invoke,
       EventName = nameof(ConnectionDiagnosticsEventIds.Invoke),
       Level = LogLevel.Debug,
       Message = "invoke complete {{ IsOneway = {IsOneway}, ResultType = {ResultType}, " +
        "LocalNetworkAddress = {LocalNetworkAddress}, RemoteNetworkAddress = {RemoteNetworkAddress}, " +
        "Time = {TotalMilliseconds} ms }}")]
    internal static partial void LogProtocolConnectionInvoke(
       this ILogger logger,
       bool isOneway,
       ResultType resultType,
       EndPoint? localNetworkAddress,
       EndPoint? remoteNetworkAddress,
       double totalMilliseconds);

    [LoggerMessage(
       EventId = (int)ConnectionDiagnosticsEventIds.InvokeException,
       EventName = nameof(ConnectionDiagnosticsEventIds.InvokeException),
       Level = LogLevel.Debug,
       Message = "invoke exception {{ IsOneway = {IsOneway}, Time = {TotalMilliseconds} ms }}")]
    internal static partial void LogProtocolConnectionInvokeException(
       this ILogger logger,
       bool isOneway,
       double totalMilliseconds,
       Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Shutdown,
        EventName = nameof(ConnectionEventIds.Shutdown),
        Level = LogLevel.Information,
        Message = "shut down {Protocol} connection for {Endpoint} over " +
            "{LocalNetworkAddress}<->{RemoteNetworkAddress} in {TotalMilliseconds:F} ms: {Message}")]
    internal static partial void LogProtocolConnectionShutdown(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        double totalMilliseconds,
        string message);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ShutdownException,
        EventName = nameof(ConnectionEventIds.ShutdownException),
        Level = LogLevel.Information,
        Message = "failed to shut down {Protocol} connection for {Endpoint} over " +
            "{LocalNetworkAddress}<->{RemoteNetworkAddress} after {TotalMilliseconds:F} ms")]
    internal static partial void LogProtocolConnectionShutdownException(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        double totalMilliseconds,
        Exception exception);

    internal static IDisposable StartInvocationScope(
        this ILogger logger,
        ServiceAddress serviceAddress,
        string operation) =>
        _invocationScope(logger, serviceAddress, operation);

    private enum ConnectionDiagnosticsEventIds
    {
        Invoke = BaseEventIds.Connection + (BaseEventIds.EventIdRange / 2),
        InvokeException
    }
}
