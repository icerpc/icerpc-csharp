// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Configure;

/// <summary>A property bag used to configure the ice protocol.</summary>
public record class IceOptions
{
    /// <summary>Gets or sets the maximum number of requests that an ice connection can dispatch concurrently.
    /// </summary>
    /// <value>The maximum number of requests that an ice connection can dispatch concurrently. 0 means no maximum.
    /// The default value is 100 requests.</value>
    public int MaxConcurrentDispatches
    {
        get => _maxConcurrentDispatches;
        set => _maxConcurrentDispatches = value >= 0 ? value :
            throw new ArgumentOutOfRangeException(nameof(value), "value must be 0 or greater");
    }

    /// <summary>Gets or sets the maximum size of an incoming ice frame.</summary>
    /// <value>The maximum size of an incoming ice frame, in bytes. This value must be at least 256. The default
    /// value is 1 MB.</value>
    public int MaxIncomingFrameSize
    {
        get => _maxIncomingFrameSize;
        set => _maxIncomingFrameSize = value >= MinIncomingFrameSizeValue ? value :
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"{nameof(MaxIncomingFrameSize)} must be at least {MinIncomingFrameSizeValue}");
    }

    /// <summary>A shared instance that holds the default options.</summary>
    /// <remarks>It's internal to avoid accidental changes to these shared default options.</remarks>
    internal static IceOptions Default { get; } = new();

    private const int MinIncomingFrameSizeValue = 256;
    private int _maxConcurrentDispatches = 100;
    private int _maxIncomingFrameSize = 1024 * 1024;
}

/// <summary>A property bag used to configure a client connection using the ice protocol.</summary>
public sealed record class IceClientOptions : IceOptions
{
    /// <summary>Returns the default value for <see cref="ClientTransport"/>.</summary>
    public static IClientTransport<ISimpleNetworkConnection> DefaultClientTransport { get; } =
        new TcpClientTransport();

    /// <summary>Gets or sets the <see cref="IClientTransport{ISimpleNetworkConnection}"/> used by this
    /// connection to create simple network connections.</summary>
    public IClientTransport<ISimpleNetworkConnection> ClientTransport { get; set; } = DefaultClientTransport;
}

/// <summary>A property bag used to configure a server connection using the ice protocol.</summary>
public sealed record class IceServerOptions : IceOptions
{
    /// <summary>Returns the default value for <see cref="ServerTransport"/>.</summary>
    public static IServerTransport<ISimpleNetworkConnection> DefaultServerTransport { get; } =
        new TcpServerTransport();

    /// <summary>Gets or sets the <see cref="IServerTransport{ISimpleNetworkConnection}"/> used by the server
    /// to accept simple connections.</summary>
    public IServerTransport<ISimpleNetworkConnection> ServerTransport { get; set; } = DefaultServerTransport;
}
