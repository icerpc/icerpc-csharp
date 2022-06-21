// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This exception indicates that a connection was aborted.</summary>
public class ConnectionAbortedException : Exception
{
    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class.</summary>
    public ConnectionAbortedException()
        : base("connection aborted")
    {
    }

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified
    /// error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public ConnectionAbortedException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified
    /// error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionAbortedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>This exception indicates that a ConnectAsync or DisposeAsync on this connection was canceled, and the
/// the connection is now in an unusable state.</summary>
public class ConnectionCanceledException : Exception
{
    /// <summary>Constructs a new instance of the <see cref="ConnectionCanceledException"/> class.</summary>
    public ConnectionCanceledException()
        : base("connection canceled")
    {
    }
}

/// <summary>This exception indicates that a previous established connection was closed gracefully. It is safe to
/// retry a request that failed with this exception.</summary>
public class ConnectionClosedException : ObjectDisposedException
{
    /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class.</summary>
    public ConnectionClosedException()
        : base($"{typeof(IConnection)}", "cannot access closed connection")
    {
    }

    /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified
    /// error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public ConnectionClosedException(string message)
        : base($"{typeof(IConnection)}", message)
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
