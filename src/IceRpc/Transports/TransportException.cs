// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This exception and its derived exceptions are thrown by transport implementation to report errors in a
    /// transport-independent manner. This is turn allows IceRPC components (such as the Retry interceptor) and
    /// application code to handle transport exception without knowning transport-specific exceptions. For example, a
    /// socket-based transport implementation catches <see cref="System.Net.Sockets.SocketException"/> and wraps
    /// them in transport exceptions.</summary>
    public class TransportException : Exception
    {
        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a specified error
        /// message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public TransportException(string message)
            : base(message)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a reference to the
        /// inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public TransportException(Exception innerException)
            : base("", innerException)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a specified error
        /// message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public TransportException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>This exception reports a failed attempt to establish a connection.</summary>
    public class ConnectFailedException : TransportException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectFailedException"/> class with a specified error
        /// message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public ConnectFailedException(string message)
            : base(message)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectFailedException"/> class with a reference to
        /// the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConnectFailedException(Exception innerException)
            : base(innerException)
        {
        }
    }

    /// <summary>This exception reports a connection refused error.</summary>
    public class ConnectionRefusedException : ConnectFailedException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectionRefusedException"/> class.</summary>
        public ConnectionRefusedException()
            : base("connection establishment was refused by the peer")
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionRefusedException"/> class with a reference
        /// to the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConnectionRefusedException(Exception innerException)
            : base(innerException)
        {
        }
    }

    /// <summary>This exception reports that a previously established connection was lost.</summary>
    public class ConnectionLostException : TransportException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectionLostException"/> class.</summary>
        public ConnectionLostException()
            : base("connection lost")
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionLostException"/> class with a reference to
        /// the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConnectionLostException(Exception innerException)
            : base("connection lost", innerException)
        {
        }
    }

    /// <summary>This exception is thrown when a multiplexed network connection is closed.</summary>
    public class MultiplexedNetworkConnectionClosedException : TransportException
    {
        /// <summary>The application error code.</summary>
        public long ApplicationErrorCode { get; }

        /// <summary>Constructs a new exception.</summary>
        /// <param name="applicationErrorCode">The application error code.</param>
        public MultiplexedNetworkConnectionClosedException(long applicationErrorCode)
            : base($"connection aborted with application error code '{applicationErrorCode}'") =>
            ApplicationErrorCode = applicationErrorCode;
    }

    /// <summary>This exception is thrown when a multiplexed stream is aborted.</summary>
    public class MultiplexedStreamAbortedException : TransportException
    {
        /// <summary>The stream error kind.</summary>
        public MultiplexedStreamErrorKind ErrorKind { get; }

        /// <summary>The stream error code.</summary>
        public int ErrorCode { get; }

        /// <summary>Constructs a new exception.</summary>
        /// <param name="errorKind">The stream error kind.</param>
        /// <param name="errorCode">The stream error code.</param>
        public MultiplexedStreamAbortedException(MultiplexedStreamErrorKind errorKind, int errorCode) :
            base($"stream aborted with error kind '{errorKind}' and error code '{errorCode}'")
        {
            ErrorKind = errorKind;
            ErrorCode = errorCode;
        }

        internal MultiplexedStreamAbortedException(long error) :
            this((MultiplexedStreamErrorKind)(error >> 32), (int)(error & (long)int.MaxValue))
        {
        }

        internal long ToError() => ((long)ErrorKind << 32) | (long)ErrorCode;
    }
}
