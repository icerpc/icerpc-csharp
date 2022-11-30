// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;
using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a multiplexed transport.</summary>
public interface IMultiplexedConnection : IAsyncDisposable
{
    /// <summary>Gets the server address of this connection. This server address Transport property is non-null.
    /// </summary>
    ServerAddress ServerAddress { get; }

    /// <summary>Accepts a remote stream. This method is never called concurrently or after a <see
    /// cref="IAsyncDisposable.DisposeAsync" /> call. It's only called after a successful <see cref="ConnectAsync" />
    /// call.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The remote stream.</returns>
    ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken);

    /// <summary>Connects this connection. This method is only called once and always before any other methods of this
    /// interface.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The <see cref="TransportConnectionInformation" />.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
    /// <exception cref="TransportException">Thrown if a transport error was encountered.</exception>
    /// <remarks>A transport implementation might raise other exceptions. A connection supporting SSL can for instance
    /// raise <see cref="AuthenticationException" /> if the authentication fails while the connection is being
    /// established.</remarks>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection. This method is only called once and before <see
    /// cref="IAsyncDisposable.DisposeAsync" />.</summary>
    /// <param name="applicationErrorCode">The application error code to transmit to the peer.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the connection is closed.</returns>
    Task CloseAsync(ulong applicationErrorCode, CancellationToken cancellationToken);

    /// <summary>Creates a local stream. The creation might be delayed if the maximum number of unidirectional or
    /// bidirectional streams prevents creating the new stream. This method is never called after a <see
    /// cref="IAsyncDisposable.DisposeAsync" /> call. It's only called after a successful <see cref="ConnectAsync" />
    /// call.</summary>
    /// <param name="bidirectional"><see langword="true"/> to create a bidirectional stream, <see langword="false"/>
    /// otherwise.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The task that completes on the local stream is created.</returns>
    ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken);
}
