// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A protocol connection enables communication over a transport connection using either the ice or icerpc
/// protocol.</summary>
internal interface IProtocolConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the endpoint that identifies this protocol connection.</summary>
    Endpoint Endpoint { get; }

    /// <summary>Connects the protocol connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The transport connection information.</returns>
    Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel);

    /// <summary>Adds a callback that will be executed when this connection is aborted.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    void OnAbort(Action<Exception> callback);

    /// <summary>Adds a callback that will be executed when this connection is shut down gracefully.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    void OnShutdown(Action<string> callback);

    /// <summary>Shuts down gracefully the connection.</summary>
    /// <param name="message">The reason of the connection shutdown.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task ShutdownAsync(string message, CancellationToken cancel = default);
}
