// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection used to send and receive requests and responses.</summary>
public interface IConnection : IInvoker
{
    /// <summary>Gets a value indicating whether a call to <see cref="IInvoker.InvokeAsync"/> can succeed after a
    /// preceding call throws <see cref="ConnectionAbortedException"/>, <see cref="ConnectionClosedException"/>, <see
    /// cref="ConnectionLostException"/><see cref="ConnectFailedException"/> or <see cref="TimeoutException"/>.
    /// </summary>
    /// <value><c>true</c> when a call to <see cref="IInvoker.InvokeAsync"/> can succeed after such an exception;
    /// <c>false</c> when a new call to <see cref="IInvoker.InvokeAsync"/> will fail.</value>
    bool IsResumable { get; }

    /// <summary>Gets the network connection information or <c>null</c> if the connection is not connected.
    /// </summary>
    NetworkConnectionInformation? NetworkConnectionInformation { get; }

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
