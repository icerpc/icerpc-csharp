// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Represents a client connection. A client creates a client connection to a remote server.</summary>
public interface IClientConnection : IConnection
{
    /// <summary>Gets the endpoint of this connection.</summary>
    Endpoint Endpoint { get; }
}
