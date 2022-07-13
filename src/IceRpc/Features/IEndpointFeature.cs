// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>A feature used by the invocation pipeline to select the endpoint to use and share this selection.</summary>
public interface IEndpointFeature
{
    /// <summary>Gets or sets the alternatives to <see cref="Endpoint"/>. It is empty when Endpoint is null.</summary>
    ImmutableList<Endpoint> AltEndpoints { get; set; }

    /// <summary>Gets or sets the main endpoint for the invocation. When retrying, it represents the endpoint that was
    /// used by the preceding attempt.</summary>
    Endpoint? Endpoint { get; set; }
}
