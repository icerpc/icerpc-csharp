// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Provides access to the connection that received a request or response.</summary>
public interface IConnectionContext
{
    /// <summary>Gets the invoker implemented by the connection.</summary>
    /// <value>The <see cref="IInvoker" /> to send requests and receive responses with the connection.</value>
    IInvoker Invoker { get; }

    /// <summary>Gets the transport connection information.</summary>
    /// <value>The <see cref="TransportConnectionInformation" /> of the connection.</value>
    TransportConnectionInformation TransportConnectionInformation { get; }
}
