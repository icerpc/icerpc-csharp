// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Collections.Immutable;

namespace IceRpc.Configure;

/// <summary>A property bag used to configure the icerpc protocol.</summary>
public record class IceRpcOptions
{
    /// <summary>Gets or sets the maximum size of the header of an incoming request, response or control frame.
    /// </summary>
    /// <value>The maximum size of the header of an incoming request, response or control frame, in bytes. The default
    /// value is 16,383, and the range of this value is 63 to 1,073,747,823.</value>
    public int MaxHeaderSize
    {
        get => _maxHeaderSize;
        set => _maxHeaderSize = CheckMaxHeaderSize(value);
    }

    /// <summary>Gets or sets the connection fields to send to the peer when establishing a connection.</summary>
    public IDictionary<ConnectionFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<ConnectionFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>The default value for <see cref="MaxHeaderSize"/>.</summary>
    internal const int DefaultMaxHeaderSize = 16_383;

    private int _maxHeaderSize = DefaultMaxHeaderSize;

    internal static int CheckMaxHeaderSize(long value) => value is >= 63 and <= 1_073_747_823 ? (int)value :
        throw new ArgumentOutOfRangeException(nameof(value), "value must be between 63 and 1,073,747,823");

}

/// <summary>A property bag used to configure a client connection using the icerpc protocol.</summary>
public sealed record class IceRpcClientOptions : IceRpcOptions
{
    /// <summary>Returns the default value for <see cref="ClientTransport"/>.</summary>
    public static IClientTransport<IMultiplexedNetworkConnection> DefaultClientTransport { get; } =
        new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets or sets the <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> used by this
    /// connection to create multiplexed network connections.</summary>
    public IClientTransport<IMultiplexedNetworkConnection> ClientTransport { get; set; } = DefaultClientTransport;
}

/// <summary>A property bag used to configure a server connection using the icerpc protocol.</summary>
public sealed record class IceRpcServerOptions : IceRpcOptions
{
    /// <summary>Returns the default value for <see cref="ServerTransport"/>.</summary>
    public static IServerTransport<IMultiplexedNetworkConnection> DefaultServerTransport { get; } =
        new SlicServerTransport(new TcpServerTransport());

    /// <summary>Gets or sets <see cref="IServerTransport{IMultiplexedNetworkConnection}"/> used by the
    /// server to accept multiplexed connections.</summary>
    public IServerTransport<IMultiplexedNetworkConnection> ServerTransport { get; set; } = DefaultServerTransport;
}
