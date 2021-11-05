// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal static class IOExceptionExtensions
    {
        /// <summary>Converts a IO exception into a <see cref="ConnectFailedException"/> or
        /// <see cref="OperationCanceledException"/>.</summary>
        internal static Exception ToConnectFailedException(this IOException exception, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
            {
                return new OperationCanceledException(null, exception, cancel);
            }

            return exception.InnerException is SocketException socketException ?
                socketException.ToConnectFailedException(cancel) : new ConnectFailedException(exception);
        }

        /// <summary>Converts an IO exception into a <see cref="TransportException"/> or
        /// <see cref="ConnectionLostException"/> or <see cref="OperationCanceledException"/>.</summary>
        internal static Exception ToTransportException(this IOException exception, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
            {
                return new OperationCanceledException(null, exception, cancel);
            }

            return exception.InnerException is SocketException socketException ?
                socketException.ToTransportException(cancel) : new ConnectionLostException(exception);
        }
    }
}
