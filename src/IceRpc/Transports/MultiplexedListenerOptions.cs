// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IMultiplexedListener"/>.</summary>
public sealed record class MultiplexedListenerOptions
{
    /// <summary>Gets or sets the <see cref="MultiplexedServerConnectionOptions"/> used to create a server <see
    /// cref="IMultiplexedConnection"/>.</summary>
    /// <value>The <see cref="MultiplexedServerConnectionOptions"/>.</value>
    public MultiplexedServerConnectionOptions ServerConnectionOptions { get; set; } = new();

    /// <summary>Gets or sets the listener's endpoint.</summary>
    public Endpoint Endpoint { get; set; }

    /// <summary>Gets or sets the listener's logger.</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;
}
