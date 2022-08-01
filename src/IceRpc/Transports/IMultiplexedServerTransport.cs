// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IMultiplexedListener"/> to accept incoming multiplexed
/// connections.</summary>
public interface IMultiplexedServerTransport
{
    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Starts listening on an endpoint.</summary>
    /// <param name="options">The listener options.</param>
    /// <returns>The new listener.</returns>
    IMultiplexedListener Listen(MultiplexedListenerOptions options);
}
