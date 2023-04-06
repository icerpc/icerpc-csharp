// Copyright (c) ZeroC, Inc.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a multiplexed transport.</summary>
/// <remarks>This interface is used by the IceRpc core. It provides a number of guarantees on how the methods from this
/// interface are called:
/// <list type="bullet">
/// <item><description>The <see cref="ConnectAsync" /> method is always called first and once. No other methods are
/// called until it completes.</description></item>
/// <item><description>The <see cref="AcceptStreamAsync" /> and <see cref="CreateStreamAsync" /> methods can be called
/// concurrently.</description></item>
/// <item><description>The <see cref="AcceptStreamAsync" /> method is never called concurrently.</description></item>
/// <item><description>The <see cref="CreateStreamAsync" /> method can be called concurrently.</description></item>
/// <item><description>The <see cref="CloseAsync" /> method is called once but not while an <see
/// cref="AcceptStreamAsync" /> call is in progress. It can be called while a <see cref="CreateStreamAsync" /> call is
/// in progress.</description></item>
/// <item><description>The <see cref="CreateStreamAsync" /> and <see cref="AcceptStreamAsync" /> methods are never
/// called after a <see cref="CloseAsync" /> call.</description></item>
/// <item><description>The <see cref="IAsyncDisposable.DisposeAsync" /> method is called once and can be called while a
/// <see cref="CreateStreamAsync" /> call is in progress.</description></item>
/// </list>
/// </remarks>
public interface IMultiplexedConnection : IAsyncDisposable
{
    /// <summary>Accepts a remote stream.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes successfully with the remote stream. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="ConnectAsync" /> did not complete successfully
    /// prior to this call.</exception>
    /// <exception cref="IceRpcException">Thrown if the connection is closed.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken);

    /// <summary>Connects this connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes successfully with transport connection information when the connection is
    /// established. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="AuthenticationException" />if authentication failed.</description></item>
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection.</summary>
    /// <param name="closeError">The error to transmit to the peer.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the connection closure completes successfully. This task can also complete
    /// with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="ConnectAsync" /> did not complete successfully
    /// prior to this call, or if this method is called more than once.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken);

    /// <summary>Creates a local stream. The creation will block if the maximum number of unidirectional or
    /// bidirectional streams prevents creating the new stream.</summary>
    /// <param name="bidirectional"><see langword="true"/> to create a bidirectional stream, <see langword="false"/>
    /// otherwise.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The task that completes on the local stream is created.</returns>
    /// <returns>A task that completes successfully with the local stream. This task can also complete with one of the
    /// following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="ConnectAsync" /> did not complete successfully
    /// prior to this call.</exception>
    /// <exception cref="IceRpcException">Thrown if the connection is closed.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken);
}
