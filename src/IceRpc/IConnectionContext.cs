// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Provides access to the connection that received a request or response.</summary>
public interface IConnectionContext
{
    /// <summary>Gets the invoker implemented by the connection.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets the network connection information.</summary>
    NetworkConnectionInformation NetworkConnectionInformation { get; }

    /// <summary>Gets the protocol of this connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Adds a callback that will be executed when the connection is aborted. The connection can be aborted
    /// by a local call to Abort, by a transport error or by a shutdown failure. If the connection is already aborted,
    /// this callback is executed synchronously with an instance of <see cref="ConnectionAbortedException"/>.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    void OnAbort(Action<Exception> callback);

    /// <summary>Adds a callback that will be executed when the connection shutdown is initiated. The connection can be
    /// shut down by a local call to ShutdownAsync or by the remote peer. If the connection is already shutting down or
    /// gracefully closed, this callback is executed synchronously with an empty message.</summary>
    /// <param name="callback">The callback to execute. Its parameter is the shutdown message. It must not block or
    /// throw any exception.</param>
    void OnShutdown(Action<string> callback);
}
