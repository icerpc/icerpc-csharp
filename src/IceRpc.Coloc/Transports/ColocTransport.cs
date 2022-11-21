// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Collections.Concurrent;

namespace IceRpc.Transports;

/// <summary>The Coloc transport class provides a client and server transport that can be used for in-process
/// communications.</summary>
public sealed class ColocTransport
{
    /// <summary>The transport name.</summary>
    public const string Name = "coloc";

    /// <summary>Gets the colocated client transport.</summary>
    public IDuplexClientTransport ClientTransport { get; }

    /// <summary>Gets the colocated server transport.</summary>
    public IDuplexServerTransport ServerTransport { get; }

    /// <summary>Constructs a <see cref="ColocTransport" />.</summary>
    public ColocTransport()
        : this(511)
    {
    }

    /// <summary>Constructs a <see cref="ColocTransport" />.</summary>
    /// <param name="listenBacklog">The maximum length of the pending connections queue.</param>
    public ColocTransport(int listenBacklog)
    {
        var listeners = new ConcurrentDictionary<ServerAddress, ColocListener>();
        ClientTransport = new ColocClientTransport(listeners);
        ServerTransport = new ColocServerTransport(listeners, listenBacklog);
    }

    internal static bool CheckParams(ServerAddress serverAddress) => serverAddress.Params.Count == 0;
}
