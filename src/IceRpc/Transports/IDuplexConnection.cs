// Copyright (c) ZeroC, Inc.

using System.Security.Authentication;

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a duplex transport.</summary>
/// <remarks>This interface is used by the IceRPC core. The IceRPC core provides a number of guarantees on how it calls
/// these methods:
/// <list type="bullet">
/// <item><description>it calls the <see cref="ConnectAsync" /> method first and once. It does not call any other method
/// until the connect task completes.</description></item>
/// <item><description>it calls the <see cref="ReadAsync" /> and <see cref="WriteAsync" /> methods, sometimes
/// concurrently. It does not make concurrent reads or concurrent writes.</description></item>
/// <item><description>it can call the <see cref="ShutdownAsync" /> method once but not while a write is in progress.
/// </description></item>
/// <item><description>it calls the <see cref="IDisposable.Dispose" /> method after the tasks returned by other methods
/// have completed. It can call this method multiple times but not concurrently.</description></item>
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
    /// <exception cref="InvalidOperationException">Thrown if the connection is not connected, already shut down or
    /// shutting down, or if a read operation is already in progress.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the connection is disposed.</exception>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Shuts down the connection.</summary>
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
    Task ShutdownAsync(CancellationToken cancellationToken);

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
