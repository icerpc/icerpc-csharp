// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection for a <see cref="Protocol"/>. It is the building block for
/// <see cref="ClientConnection"/>, <see cref="ConnectionCache"/> and the connections created by <see cref="Server"/>.
/// Applications can use this interface to build their own custom client connection and connection cache classes.
/// </summary>
public interface IProtocolConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport"/> property is always
    /// non-null.</value>
    ServerAddress ServerAddress { get; }

    /// <summary>Gets a task that completes when the connection is shut down, aborted or lost.</summary>
    /// <value>A task that completes with the shutdown message when the connection is successfully shut down. It
    /// completes with an exception when the connection is aborted or lost.</value>
    Task<string> ShutdownComplete { get; }

    /// <summary>Establishes the connection to the peer.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the connection is established  provides the <see
    /// cref="TransportConnectionInformation"/> for this connection. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionException"/>if the connection establishment failed.</description></item>
    /// <item><description><see cref="OperationCanceledException"/>if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ConnectionException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the peer with the icerpc protocol.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionException"/>if the connection shutdown failed.</description></item>
    /// <item><description><see cref="OperationCanceledException"/>if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException"/>if this shutdown attempt or a previous attempt exceeded
    /// <see cref="ConnectionOptions.ShutdownTimeout"/>.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ConnectionException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task ShutdownAsync(string message, CancellationToken cancellationToken = default);
}
