// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports;

/// <summary>The base interface for listeners.</summary>
public interface IListener : IAsyncDisposable
{
    /// <summary>Gets the server address of this listener. That's the address a client would connect to.</summary>
    /// <value>The server address.</value>
    ServerAddress ServerAddress { get; }
}

/// <summary>A listener listens for connection requests from clients.</summary>
/// <typeparam name="T">The connection type.</typeparam>
/// <remarks>The IceRPC core and Slice transport implementation uses this interface. It provides the following
/// guarantees:
/// <list type="bullet">
/// <item><description>The <see cref="AcceptAsync" /> method is never called concurrently.</description></item>
/// <item><description>The <see cref="IAsyncDisposable.DisposeAsync" /> method can be called while an <see
/// cref="AcceptAsync" /> call is in progress. It can be called multiple times but not
/// concurrently.</description></item>
/// </list>
/// </remarks>
public interface IListener<T> : IListener
{
    /// <summary>Accepts a new connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes successfully with the accepted connection and network address of the client. This
    /// task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="IceRpcException" />if the transport reported an error.</description></item>
    /// <item><description><see cref="OperationCanceledException" />if cancellation was requested through the
    /// cancellation token.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
    Task<(T Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(CancellationToken cancellationToken);
}
