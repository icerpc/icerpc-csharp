// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Security.Authentication;

namespace IceRpc;

/// <summary>Represents a connection for a <see cref="Protocol" />. It is the building block for <see
/// cref="ClientConnection" />, <see cref="ConnectionCache" /> and the connections created by <see cref="Server" />.
/// Applications can use this interface to build their own custom client connection and connection cache classes.
/// </summary>
/// <remarks>The disposal of the protocol connection aborts invocations, cancels dispatches and disposes the underlying
/// transport connection without waiting for the peer. To wait for invocations and dispatches to complete, call <see
/// cref="ShutdownAsync" /> first. If the configured dispatcher does not complete promptly when its cancellation token
/// is canceled, the disposal can hang.</remarks>
/// <seealso cref="ClientProtocolConnectionFactory" />
public interface IProtocolConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Establishes the connection to the peer.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that provides the <see cref="TransportConnectionInformation" /> of the transport connection
    /// and a task that completes when the connection itself wants to be shut down then disposed by the caller. This can
    /// happen when the peer initiates a shutdown, when the connection is inactive for too long (see
    /// <see cref="ConnectionOptions.InactivityTimeout" />), when the connection detects a protocol violation, or when
    /// the connection gets an error from its transport connection. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="AuthenticationException" />if authentication failed.</description></item>
    /// <item><description><see cref="IceRpcException" />if the connection establishment failed.</description>
    /// </item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken = default);

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
    /// <exception cref="InvalidOperationException">Thrown if <see cref="ConnectAsync" /> did not complete successfully
    /// prior to this call, or if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this connection is disposed.</exception>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
