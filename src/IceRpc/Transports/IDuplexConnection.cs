// Copyright (c) ZeroC, Inc.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a duplex transport.</summary>
/// <remarks>Both the IceRPC core and the Slic transport implementation use this interface. They provide the following
/// guarantees:
/// <list type="bullet">
/// <item><description>The <see cref="ConnectAsync" /> method is called first and once. No other methods are called
/// until it completes.</description></item>
/// <item><description>The <see cref="ReadAsync" /> method is never called concurrently.</description></item>
/// <item><description>The <see cref="WriteAsync" /> method is never called concurrently.</description></item>
/// <item><description>The <see cref="ReadAsync" /> and <see cref="WriteAsync" /> methods can be called concurrently.
/// </description></item>
/// <item><description>The <see cref="ReadAsync" /> and <see cref="ShutdownWriteAsync" /> methods can be called
/// concurrently.
/// </description></item>
/// <item><description>The <see cref="ShutdownWriteAsync" /> method is called once but not while a <see
/// cref="WriteAsync" /> call is in progress.</description></item>
/// <item><description>The <see cref="WriteAsync" /> is never called after a <see cref="ShutdownWriteAsync" />
/// call.</description></item>
/// <item><description>The <see cref="IDisposable.Dispose" /> method is called after the tasks returned by other methods
/// have completed. It can be called multiple times but not concurrently.</description></item>
/// </list>
/// </remarks>
public interface IDuplexConnection : IDisposable
{
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
    /// <exception cref="InvalidOperationException">Thrown if this connection is connected, connecting or if a previous
    /// connection attempt failed.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection is disposed.</exception>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Reads data from the connection.</summary>
    /// <param name="buffer">A buffer that receives the data read from the connection.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes successfully with the number of bytes read into <paramref name="buffer" />.
    /// This number is always greater than <c>0</c>. This value task can also complete with one of the following
    /// exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="buffer" /> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the connection is not connected or if a read operation is
    /// already in progress.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection is disposed.</exception>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Shuts down the write side of the connection to notify the peer that no more data will be
    /// sent.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes successfully when the shutdown completes successfully. This task can also
    /// complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if the connection is not connected, already shut down or
    /// shutting down, or a write operation is in progress.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection is disposed.</exception>
    Task ShutdownWriteAsync(CancellationToken cancellationToken);

    /// <summary>Writes data over the connection.</summary>
    /// <param name="buffers">The buffers containing the data to write.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes successfully when the data is written successfully. This value task can
    /// also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="buffers" /> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the connection is not connected, already shut down or
    /// shutting down, or a write operation is already in progress.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection is disposed.</exception>
    ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken);
}
