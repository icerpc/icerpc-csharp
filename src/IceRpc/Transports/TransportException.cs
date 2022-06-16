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

        /// <summary>Constructs a new instance of the <see cref="ConnectionLostException"/> class.</summary>
        /// <param name="message">The message that describes the error.</param>
        public ConnectionLostException(string message)
            : base(message)
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
}
