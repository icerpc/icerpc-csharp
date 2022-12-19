// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Provides access to the connection that received a request or response.</summary>
public interface IConnectionContext
{
    /// <summary>Gets the invoker implemented by the connection.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport" /> property is always
    /// non-null.</value>
    ServerAddress ServerAddress { get; }

    /// <summary>Gets a task that completes when the underlying transport connection is closed.</summary>
    /// <value>A task that completes successfully with a null exception when the connection is shut down gracefully.
    /// When this connection is aborted, this task completes successfully with the exception that caused the abort.
    /// This task is never faulted.</value>
    // TODO: rename property
    Task<Exception?> ShutdownComplete { get; }

    /// <summary>Gets the transport connection information.</summary>
    TransportConnectionInformation TransportConnectionInformation { get; }
}
