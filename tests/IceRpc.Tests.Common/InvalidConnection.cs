// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests;

/// <summary>Provides invalid shared connections. Each connection is protocol-specific.</summary>
public static class InvalidConnection
{
    /// <summary>An invalid ice connection.</summary>
    public static Connection Ice { get; } = new("ice://invalidhost");

    /// <summary>An invalid icerpc connection.</summary>
    public static Connection IceRpc { get; } = new("icerpc://invalidhost");
}
