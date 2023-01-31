// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Provides access to the connection that received a request or response.</summary>
public interface IConnectionContext
{
    /// <summary>Gets the invoker implemented by the connection.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets the transport connection information.</summary>
    TransportConnectionInformation TransportConnectionInformation { get; }
}
