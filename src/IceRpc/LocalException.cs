// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    /// <summary>This exception reports an attempt to use a destroyed <see cref="ConnectionPool"/>.</summary>
    public class ConnectionPoolDisposedException : ObjectDisposedException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectionPoolDisposedException"/> class.</summary>
        public ConnectionPoolDisposedException()
            : base(objectName: null, message: "communicator shutdown")
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionPoolDisposedException"/> class with a
        /// reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConnectionPoolDisposedException(Exception innerException)
            : base($"{typeof(ConnectionPool).FullName}", innerException)
        {
        }
    }

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
        /// <summary>The exception retry policy.</summary>
        internal RetryPolicy RetryPolicy { get; }

        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class. A plain
        /// TransportException should have a custom message or an inner exception (or both).</summary>
        /// <param name="retryPolicy">The exception retry policy.</param>
        protected TransportException(RetryPolicy retryPolicy = default) => RetryPolicy = retryPolicy;

        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a specified error
        /// message.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public TransportException(string message, RetryPolicy retryPolicy = default)
            : base(message) => RetryPolicy = retryPolicy;

        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a reference to the
        /// inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public TransportException(Exception innerException, RetryPolicy retryPolicy = default)
            : base("", innerException) => RetryPolicy = retryPolicy;

        /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public TransportException(
            string message,
            Exception innerException,
            RetryPolicy retryPolicy = default)
            : base(message, innerException) => RetryPolicy = retryPolicy;
    }

    /// <summary>This exception reports a failed attempt to establish a connection.</summary>
    public class ConnectFailedException : TransportException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectFailedException"/> class.</summary>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectFailedException(RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectFailedException"/> class with a reference to
        /// the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectFailedException(Exception innerException, RetryPolicy retryPolicy = default)
            : base(innerException, retryPolicy)
        {
        }
    }

    /// <summary>This exception indicates a connection establishment timeout condition.</summary>
    public class ConnectTimeoutException : ConnectFailedException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectTimeoutException"/> class.</summary>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectTimeoutException(RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
        }
    }

    /// <summary>This exception reports a connection refused error.</summary>
    public class ConnectionRefusedException : ConnectFailedException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectionRefusedException"/> class.</summary>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectionRefusedException(RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionRefusedException"/> class with a reference
        /// to the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectionRefusedException(Exception innerException, RetryPolicy retryPolicy = default)
            : base(innerException, retryPolicy)
        {
        }
    }

    /// <summary>This exception reports that a previously established connection was lost.</summary>
    public class ConnectionLostException : TransportException
    {
        /// <summary>Constructs a new instance of the <see cref="ConnectionLostException"/> class.</summary>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectionLostException(RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
        }

        /// <summary>Constructs a new instance of the <see cref="ConnectionLostException"/> class with a reference to
        /// the inner exception that is the cause of this exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The exception retry policy.</param>
        public ConnectionLostException(Exception innerException, RetryPolicy retryPolicy = default)
            : base(innerException, retryPolicy)
        {
        }
    }

    /// <summary>This exception indicates that a previous established connection was closed.</summary>
    public class ConnectionClosedException : TransportException
    {
        /// <summary><c>true</c> if the connection closure originated from the peer, <c>false</c> otherwise.</summary>
        public bool IsClosedByPeer { get; }

        /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class.</summary>
        /// <param name="isClosedByPeer"><c>true</c> if the connection closure originated from the peer, <c>false</c>
        /// otherwise</param>
        public ConnectionClosedException(bool isClosedByPeer = false)
            : base("cannot access closed connection", RetryPolicy.AfterDelay(TimeSpan.Zero)) =>
            IsClosedByPeer = isClosedByPeer;

        /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified
        /// error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="isClosedByPeer"><c>true</c> if the connection closure originated from the peer, <c>false</c>
        /// otherwise</param>
        public ConnectionClosedException(string message, bool isClosedByPeer = false)
            : base(message, RetryPolicy.AfterDelay(TimeSpan.Zero)) => IsClosedByPeer = isClosedByPeer;
    }

    /// <summary>This exception reports that data (bytes) received by Ice are not in an expected format.</summary>
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
