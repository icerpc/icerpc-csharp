// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    /// <summary>This exception reports that a proxy has no endpoint or no usable endpoint.</summary>
    public class NoEndpointException : Exception
    {
        /// <summary>Constructs a new instance of the <see cref="NoEndpointException"/> class.</summary>
        public NoEndpointException()
        {
        }

        /// <summary>Constructs a new instance of the <see cref="NoEndpointException"/> class.</summary>
        /// <param name="proxy">The proxy with no endpoint or no usable endpoint.</param>
        public NoEndpointException(IServicePrx proxy)
            : base($"proxy '{proxy}' has no usable endpoint")
        {
        }
    }

    /// <summary>This exception reports an error from the transport layer.</summary>
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

    /// <summary>This exception indicates a connection establishment timeout condition.</summary>
    public class ConnectTimeoutException : ConnectFailedException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectTimeoutException"/> class.</summary>
        public ConnectTimeoutException()
            : base("connection establishment timed out")
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

    /// <summary>This exception indicates that a previous established connection was closed.</summary>
    public class ConnectionClosedException : TransportException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class.</summary>
        public ConnectionClosedException()
            : base("cannot access closed connection")
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified
        /// error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public ConnectionClosedException(string message)
            : base(message)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified
        /// error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConnectionClosedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>This exception reports that data (bytes) received are not in an expected format.</summary>
    public class InvalidDataException : Exception
    {
        /// <summary>Constructs a new instance of the <see cref="InvalidDataException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public InvalidDataException(string message)
            : base(message)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="InvalidDataException"/> class with a specified error
        /// message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public InvalidDataException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
