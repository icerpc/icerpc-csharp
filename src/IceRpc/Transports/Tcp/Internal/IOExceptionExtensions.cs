// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.Tcp.Internal;

internal static class IOExceptionExtensions
{
    /// <summary>Extension methods for <see cref="IOException" />.</summary>
    /// <param name="exception">The exception to convert.</param>
    extension(IOException exception)
    {
        /// <summary>Converts an IOException into an <see cref="IceRpcException" />.</summary>
        internal IceRpcException ToIceRpcException() =>
            exception.InnerException is SocketException socketException ?
                socketException.ToIceRpcException(exception) :
                new IceRpcException(IceRpcError.IceRpcError, exception);
    }
}
