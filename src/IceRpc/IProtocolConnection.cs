// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Represents a connection for a <see cref="Protocol" />. It is the building block for <see
/// cref="ClientConnection" />, <see cref="ConnectionCache" /> and the connections created by <see cref="Server" />.
/// Applications can use this interface to build their own custom client connection and connection cache classes.
/// </summary>
/// <seealso cref="ClientProtocolConnectionFactory" />
public interface IProtocolConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets a task that completes when the connection is closed after being successfully established.
    /// </summary>
    /// <value>A task that completes when the connection is closed. If the connection was shut down gracefully, this
    /// task completes with a null exception; otherwise, it completes with the exception that aborted the connection.
    /// </value>
    /// <remarks>This task is never faulted or canceled.</remarks>
    Task<Exception?> Closed { get; }

    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport" /> property is always
    /// non-null.</value>
    ServerAddress ServerAddress { get; }

    /// <summary>Gets a task that completes when the peer or the inactivity monitor requests the shutdown of this
    /// connection.</summary>
    /// <remarks>This task is never faulted or canceled.</remarks>
    /// <seealso cref="ConnectionOptions.InactivityTimeout" />
    Task ShutdownRequested { get; }

    /// <summary>Establishes the connection to the peer.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that provides the <see cref="TransportConnectionInformation" /> of the transport connection,
    /// once this connection is established. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the connection establishment failed.</description>
    /// </item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="IceRpcException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the connection shutdown failed.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="IceRpcException">Thrown if the connection is closed but not disposed yet.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="ConnectAsync" /> did not complete successfully
    /// prior to this call, or if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
