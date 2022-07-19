// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IDuplexListener"/>.</summary>
public sealed record class DuplexListenerOptions
{
    /// <summary>Gets or sets the <see cref="DuplexServerConnectionOptions"/> used to create a server <see
    /// cref="IDuplexConnection"/>.</summary>
    /// <value>The <see cref="MultiplexedServerConnectionOptions"/>.</value>
    public DuplexServerConnectionOptions ServerConnectionOptions { get; set; } = new();

    /// <summary>Gets or sets the listener's endpoint.</summary>
    public Endpoint Endpoint { get; set; }

    /// <summary>Gets or sets the listener's logger.</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;
}
