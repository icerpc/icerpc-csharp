// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Slice
{
    /// <summary>Holds properties to customize a request and to get back information from the corresponding response.
    /// </summary>
    public sealed class Invocation
    {
        /// <summary>Gets or sets the features carried by the request.</summary>
        /// <remarks>These features are updated (set) when the response is received.</remarks>
        public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;
    }
}
