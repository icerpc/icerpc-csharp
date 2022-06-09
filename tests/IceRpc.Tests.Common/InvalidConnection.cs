// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

/// <summary>Provides invalid shared connections. Each connection is protocol-specific.</summary>
public static class InvalidConnection
{
    /// <summary>An invalid ice connection.</summary>
    public static IConnection Ice { get; } = new ClientConnection("ice://host?transport=invalid");

    /// <summary>An invalid icerpc connection.</summary>
    public static IConnection IceRpc { get; } = new ClientConnection("icerpc://host?transport=invalid");

    /// <summary>Returns the invalid connection for the given protocol.</summary>
    public static IConnection ForProtocol(Protocol protocol) => protocol == Protocol.Ice ? Ice : IceRpc;
}
