// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal static class SocketExceptionExtensions
    {
        /// <summary>Converts a socket exception into a <see cref="ConnectFailedException"/> or
        /// <see cref="OperationCanceledException"/>.</summary>
        internal static Exception ToConnectFailedException(this SocketException exception, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
            {
                return new OperationCanceledException(null, exception, cancel);
            }

            return exception.SocketErrorCode == SocketError.ConnectionRefused ?
                new ConnectionRefusedException(exception) : new ConnectFailedException(exception);
        }

        /// <summary>Converts a socket exception into a <see cref="TransportException"/>,
        /// <see cref="ConnectionLostException"/> or <see cref="OperationCanceledException"/>.</summary>
        internal static Exception ToTransportException(this SocketException exception, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
            {
                return new OperationCanceledException(null, exception, cancel);
            }

            SocketError error = exception.SocketErrorCode;
            if (error == SocketError.ConnectionReset ||
                error == SocketError.Shutdown ||
                error == SocketError.ConnectionAborted ||
                error == SocketError.NetworkDown ||
                error == SocketError.NetworkReset)
            {
                return new ConnectionLostException(exception);
            }
            else
            {
                return new TransportException(exception);
            }
        }
    }
}
