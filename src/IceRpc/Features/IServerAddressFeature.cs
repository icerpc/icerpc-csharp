// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>A feature used by the invocation pipeline to select the server address to use and share this selection.
/// </summary>
public interface IServerAddressFeature
{
    /// <summary>Gets or sets the alternatives to <see cref="ServerAddress" />.</summary>
    /// <value>The list of alternatives <see cref="IceRpc.ServerAddress" />. It is empty when <see cref="ServerAddress"
    /// /> is <see langword="null" />.
    ImmutableList<ServerAddress> AltServerAddresses { get; set; }

    /// <summary>Gets or sets the list of <see cref="IceRpc.ServerAddress" /> that have been removed and will not be
    /// used for the invocation.</summary>
    /// <value>The list of removed <see cref="IceRpc.ServerAddress" />.</value>
    ImmutableList<ServerAddress> RemovedServerAddresses { get; set; }

    /// <summary>Gets or sets the main server address for the invocation. When retrying, it represents the server
    /// address that was used by the preceding attempt.</summary>
    /// <value>The main server address. It is <see langword="null" /> if there's no more server address to use for the
    /// invocation.</value>
    ServerAddress? ServerAddress { get; set; }
}
