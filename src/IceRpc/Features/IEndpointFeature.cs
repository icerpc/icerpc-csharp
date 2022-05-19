// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>A feature used by the invocation pipeline to select the target endpoint.</summary>
public interface IEndpointFeature
{
    /// <summary>Gets or sets the alternatives to <see cref="Endpoint"/>. It is empty when Endpoint is null.</summary>
    IEnumerable<Endpoint> AltEndpoints { get; set; }

    /// <summary>Gets or sets the main target endpoint for the invocation.</summary>
    Endpoint? Endpoint { get; set; }
}
