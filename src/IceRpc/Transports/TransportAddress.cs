// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Transports;

/// <summary>Represents a transport address with the host and port to connect to or listen on.</summary>
public readonly record struct TransportAddress
{
    /// <summary>Gets or initializes the host.</summary>
    /// <value>The host name or IP address.</value>
    public required string Host { get; init; }

    /// <summary>Gets or initializes the port number.</summary>
    /// <value>The port number.</value>
    public ushort Port { get; init; }

    /// <summary>Gets or initializes the transport name.</summary>
    /// <value>The transport name (e.g., "tcp", "ssl", "quic"), or <see langword="null" /> if unspecified.</value>
    public string? TransportName { get; init; }

    /// <summary>Gets or initializes the transport-specific parameters.</summary>
    /// <value>The transport parameters. Defaults to an empty dictionary.</value>
    public ImmutableDictionary<string, string> Params { get; init; } = [];

    /// <summary>Constructs a transport address.</summary>
    public TransportAddress()
    {
    }
}
