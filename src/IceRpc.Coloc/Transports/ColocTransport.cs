// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Collections.Concurrent;

namespace IceRpc.Transports;

/// <summary>The Coloc transport class provides a client and server duplex transport that can be used for in-process
/// communications.</summary>
public sealed class ColocTransport
{
    /// <summary>The transport name.</summary>
    public const string Name = "coloc";

    /// <summary>Gets the colocated client transport.</summary>
    /// <value>The client transport.</value>
    public IDuplexClientTransport ClientTransport { get; }

    /// <summary>Gets the colocated server transport.</summary>
    /// <value>The server transport.</value>
    public IDuplexServerTransport ServerTransport { get; }

    /// <summary>Constructs a <see cref="ColocTransport" />.</summary>
    public ColocTransport()
        : this(new ColocTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="ColocTransport" />.</summary>
    /// <param name="options">The options to configure the Coloc transport.</param>
    public ColocTransport(ColocTransportOptions options)
    {
        var listeners = new ConcurrentDictionary<ServerAddress, ColocListener>();
        ClientTransport = new ColocClientTransport(listeners, options);
        ServerTransport = new ColocServerTransport(listeners, options);
    }

    internal static bool CheckParams(ServerAddress serverAddress) => serverAddress.Params.Count == 0;
}
