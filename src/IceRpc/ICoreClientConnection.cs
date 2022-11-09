// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a client connection. It is the building block for <see cref="ClientConnection" />, <see
/// cref="ConnectionCache" />. Applications can use this interface to build their own custom client connection and
/// connection cache classes.
/// </summary>
public interface ICoreClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport" /> property is always
    /// non-null.</value>
    ServerAddress ServerAddress { get; }

    /// <summary>Gets a task that completes when the connection is shut down or fails.</summary>
    /// <value>A task that completes when the connection is successfully shut down. It completes with an exception when
    /// the connection fails.</value>
    Task ShutdownComplete { get; }

    /// <summary>Establishes the connection to the peer.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once this connection is established. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionException" />if the connection establishment failed.</description>
    /// </item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if the connection establishment attempt exceeded <see
    /// cref="ConnectionOptions.ConnectTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ConnectionException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Gracefully shuts down the connection. The shutdown waits for pending invocations and dispatches to
    /// complete. For a speedier graceful shutdown, call <see cref="IAsyncDisposable.DisposeAsync" /> instead. It will
    /// cancel pending invocations and dispatches.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionException" />if the connection shutdown failed.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException" />if this shutdown attempt or a previous attempt exceeded <see
    /// cref="ConnectionOptions.ShutdownTimeout" />.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ConnectionException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    /// <remarks>If shutdown is canceled, the connection transitions to a faulted state and the disposal of the
    /// connection will abort the connection instead of performing a graceful speedy-shutdown.</remarks>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
