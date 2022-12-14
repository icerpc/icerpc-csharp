// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class IOExceptionExtensions
{
    /// <summary>Converts an IOException into an <see cref="IceRpcException" />.</summary>
    internal static IceRpcException ToIceRpcException(this IOException exception, string? message = null) =>
        exception.InnerException is SocketException socketException ?
            socketException.ToIceRpcException(message) : new IceRpcException(IceRpcError.IceRpcError, message, exception);
}
