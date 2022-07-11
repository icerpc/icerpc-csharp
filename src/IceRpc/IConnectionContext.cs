// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Provides access to the connection that received a request or response.</summary>
public interface IConnectionContext
{
    /// <summary>Gets the invoker implemented by the connection.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets the network connection information.</summary>
    NetworkConnectionInformation NetworkConnectionInformation { get; }

    /// <summary>Gets the protocol of this connection.</summary>
    Protocol Protocol { get; }

    /// <summary>Adds a callback that will be executed when the connection is lost due to a transport failure.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    // TODO: fix doc-comment, in particular explain what happens when the connection is already closed.
    void OnAbort(Action<Exception> callback);

    /// <summary>Adds a callback that will be executed when the connection shutdown is initiated by the peer or by
    /// the connection's idle timeout.</summary>
    /// <param name="callback">The callback to execute. Its parameter is the shutdown message. It must not block or
    /// throw any exception.</param>
    // TODO: clarify doc-comment - what happens if the connection is being shut down or was shut down by the peer or
    // idle timeout?
    void OnShutdown(Action<string> callback);
}
