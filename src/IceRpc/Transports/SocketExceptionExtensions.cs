// Copyright (c) ZeroC, Inc.

// TODO: temporary, for paramref. See #4220.
#pragma warning disable CS1734 // XML comment has a type parameter reference that is not valid.

using System.Net.Sockets;

namespace IceRpc.Transports;

/// <summary>Provides an extension method for <see cref="SocketException"/> to convert it into an
/// <see cref="IceRpcException"/>.</summary>
public static class SocketExceptionExtensions
{
    /// <summary>Extension methods for <see cref="SocketException" />.</summary>
    /// <param name="exception">The exception to convert.</param>
    extension(SocketException exception)
    {
        /// <summary>Converts a <see cref="SocketException"/> into an <see cref="IceRpcException" />.</summary>
        /// <param name="innerException">The inner exception for the <see cref="IceRpcException"/>, when
        /// <see langword="null"/> <paramref name="exception"/> is used as the inner exception.</param>
        /// <returns>The <see cref="IceRpcException"/> created from the <see cref="SocketException"/>.</returns>
        public IceRpcException ToIceRpcException(Exception? innerException = null)
        {
            innerException ??= exception;
            IceRpcError errorCode = exception.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => IceRpcError.AddressInUse,
                SocketError.ConnectionAborted => IceRpcError.ConnectionAborted,
                // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the
                // peer closing non-gracefully the connection. EPIPE is returned if the socket is closed and
                // the send buffer is empty while ECONNRESET is returned if the send buffer is not empty.
                SocketError.ConnectionReset => IceRpcError.ConnectionAborted,
                SocketError.HostUnreachable => IceRpcError.ServerUnreachable,
                SocketError.NetworkUnreachable => IceRpcError.ServerUnreachable,
                SocketError.Shutdown => IceRpcError.ConnectionAborted,
                SocketError.ConnectionRefused => IceRpcError.ConnectionRefused,
                SocketError.OperationAborted => IceRpcError.OperationAborted,
                _ => IceRpcError.IceRpcError
            };

            return new IceRpcException(errorCode, innerException);
        }
    }
}
