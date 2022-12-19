// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection for a <see cref="Protocol" />. It is the building block for <see
/// cref="ClientConnection" />, <see cref="ConnectionCache" /> and the connections created by <see cref="Server" />.
/// Applications can use this interface to build their own custom client connection and connection cache classes.
/// </summary>
public interface IProtocolConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport" /> property is always
    /// non-null.</value>
    ServerAddress ServerAddress { get; }

    /// <summary>Gets a task that completes when the connection is shut down or fails. The connection shutdown is
    /// initiated by any of the following events:
    /// <list type="bullet">
    /// <item><description>The application calls <see cref="ShutdownAsync" /> on the connection.</description></item>
    /// <item><description>The connection shuts down itself because it remained idle for longer than its configured idle
    /// timeout.</description></item>
    /// <item><description>The peer shuts down the connection.</description></item>
    /// </list>
    /// </summary>
    /// <value>A task that completes when the connection is successfully shut down. It completes with an exception when
    /// the connection fails.</value>
    Task ShutdownComplete { get; }

    /// <summary>Establishes the connection to the peer.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that provides the <see cref="TransportConnectionInformation" /> of the transport connection,
    /// once this connection is established. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the connection establishment failed.</description>
    /// </item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if the connection establishment attempt exceeded <see
    /// cref="ConnectionOptions.ConnectTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="IceRpcException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Gracefully shuts down the connection. The shutdown waits for pending invocations and dispatches to
    /// complete. For a speedier graceful shutdown, call <see cref="IAsyncDisposable.DisposeAsync" /> instead. It will
    /// cancel pending invocations and dispatches.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the connection shutdown failed.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if this shutdown attempt or a previous attempt exceeded <see
    /// cref="ConnectionOptions.ShutdownTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="IceRpcException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the connection was not connected successfully prior to
    /// call.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <remarks>If shutdown is canceled, the protocol connection transitions to a faulted state and the disposal of the
    /// connection will abort the connection instead of performing a graceful speedy-shutdown.</remarks>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
