// Copyright (c) ZeroC, Inc.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a multiplexed transport.</summary>
/// <remarks>This interface is used by the IceRpc core. It provides a number of guarantees on how the methods from this
/// interface are called:
/// <list type="bullet">
/// <item><description>the <see cref="ConnectAsync" /> method is always called first and once. No other methods are
/// called until it completes.</description></item>
/// <item><description>the <see cref="AcceptStreamAsync" /> method is never called concurrently.</description></item>
/// <item><description>the <see cref="CreateStreamAsync" /> method can be called concurrently.</description></item>
/// <item><description>the <see cref="CloseAsync" /> method is only called once.</description></item>
/// <item><description>the <see cref="IAsyncDisposable.DisposeAsync" /> and <see cref="CreateStreamAsync" /> methods can
/// be called concurrently.</description></item>
/// </list>
/// </remarks>
public interface IMultiplexedConnection : IAsyncDisposable
{
    /// <summary>Accepts a remote stream.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The remote stream.</returns>
    ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken);

    /// <summary>Connects this connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The <see cref="TransportConnectionInformation" />.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    /// <exception cref="IceRpcException">Thrown if a transport error was encountered.</exception>
    /// <remarks>A transport implementation might raise other exceptions. A connection supporting SSL can for instance
    /// raise <see cref="AuthenticationException" /> if the authentication fails while the connection is being
    /// established.</remarks>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection.</summary>
    /// <param name="closeError">The error to transmit to the peer.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the connection is closed.</returns>
    Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken);

    /// <summary>Creates a local stream. The creation will block if the maximum number of unidirectional or
    /// bidirectional streams prevents creating the new stream.</summary>
    /// <param name="bidirectional"><see langword="true"/> to create a bidirectional stream, <see langword="false"/>
    /// otherwise.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The task that completes on the local stream is created.</returns>
    ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken);
}
