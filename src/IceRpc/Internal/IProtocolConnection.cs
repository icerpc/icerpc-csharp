// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A protocol connection enables communication over a network connection using either the ice or icerpc
/// protocol.</summary>
internal interface IProtocolConnection : IAsyncDisposable
{
    /// <summary>Gets the protocol implemented by this protocol connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Connects the protocol connection.</summary>
    /// <param name="connection">The value for <see cref="IncomingFrame.Connection"/> in incoming requests created by
    /// this protocol connection.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The network connection information.</returns>
    /// <remarks>This method should be called only once.</remarks>
    Task<NetworkConnectionInformation> ConnectAsync(IConnection connection, CancellationToken cancel);

    /// <summary>Sends a request and returns the response. The implementation must complete the request payload and
    /// payload stream.</summary>
    /// <param name="request">The outgoing request to send.</param>
    /// <param name="connection">The value for <see cref="IncomingFrame.Connection"/> in incoming responses created by
    /// this protocol connection.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The received response.</returns>
    Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        IConnection connection,
        CancellationToken cancel = default);

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
    /// <remarks>This method should be called only once and always after a successful <see
    /// cref="ConnectAsync"/>.</remarks>
    Task ShutdownAsync(string message, CancellationToken cancel = default);
}
