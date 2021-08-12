// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal static class ExceptionExtensions
    {
        /// <summary>This method tries to distinguish connection loss error conditions from other error conditions.
        /// It's a bit tedious since it's difficult to have an exhaustive list of errors that match this condition.
        /// An alternative would be to change the transports to always throw ConnectionLostException on failure to
        /// receive or send data.</summary>
        internal static bool IsConnectionLost(this Exception ex)
        {
            if (ex is ConnectionLostException)
            {
                return true;
            }
            else if (ex is TransportException)
            {
                return false;
            }

            // Check the inner exceptions if the given exception isn't a socket exception. Streams wrapping a socket
            // typically throw an IOException with the SocketException as the InnerException.
            while (!(ex is SocketException || ex is System.ComponentModel.Win32Exception) &&
                   ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            if (ex is SocketException socketException)
            {
                SocketError error = socketException.SocketErrorCode;
                return error == SocketError.ConnectionReset ||
                       error == SocketError.Shutdown ||
                       error == SocketError.ConnectionAborted ||
                       error == SocketError.NetworkDown ||
                       error == SocketError.NetworkReset;
            }
            else if (ex is System.ComponentModel.Win32Exception)
            {
                // "Authentication failed because the remote party has closed the transport stream"
                // "An authentication error has occurred"
                return ex.HResult == -2146232800 || ex.HResult == -2147467259;
            }
            else if (ex is System.IO.IOException)
            {
                // On Unix platforms, IOException can be raised without inner socket exception. This is
                // in particular the case for SSLStream reads.
                return true;
            }
            return false;
        }

        /// <summary>Converts an exception into a <see cref="TransportException"/> or
        /// <see cref="OperationCanceledException"/>.</summary>
        internal static Exception ToTransportException(this Exception exception, CancellationToken cancel) =>
            exception switch
            {
                OperationCanceledException ex => ex,
                TransportException ex => ex,
                Exception ex when cancel.IsCancellationRequested => new OperationCanceledException(null, ex, cancel),
                Exception ex when ex.IsConnectionLost() => new ConnectionLostException(ex),
                _ => new TransportException(exception)
            };
    }
}
