// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>A feature used by the invocation pipeline to select the server address to use and share this selection.
/// </summary>
public interface IServerAddressFeature
{
    /// <summary>Gets or sets the alternatives to <see cref="ServerAddress" />. It is empty when ServerAddress is null.
    /// </summary>
    ImmutableList<ServerAddress> AltServerAddresses { get; set; }

    /// <summary>Gets or sets a value indicating whether this server address feature was retrieved from a cache.
    /// </summary>
    /// <value>When <see langword="true" />, this feature was retrieved from a cache and corresponds to a cached
    /// resolution. Otherwise, <see langword="false" />.</value>
    bool IsFromCache { get; set; }

    /// <summary>Gets or sets the list of <see cref="ServerAddress" /> that have been removed and will not be used for
    /// the invocation.</summary>
    ImmutableList<ServerAddress> RemovedServerAddresses { get; set; }

    /// <summary>Gets or sets the main server address for the invocation. When retrying, it represents the server
    /// address that was used by the preceding attempt.</summary>
    ServerAddress? ServerAddress { get; set; }
}
