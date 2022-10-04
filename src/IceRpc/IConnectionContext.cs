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

    /// <summary>Gets a task that completes when the connection is shut down or aborted.</summary>
    /// <value>A task that completes with the shutdown message when the connection is successfully shut down. It
    /// completes with an exception when the connection is aborted.</value>
    Task<string> ShutdownComplete { get; }

    /// <summary>Gets the transport connection information.</summary>
    TransportConnectionInformation TransportConnectionInformation { get; }
}
