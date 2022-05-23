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

        /// <summary>Gets or sets whether a void-returning request is oneway. This property has no effect for operations
        /// defined in Slice that return a value.</summary>
        /// <value>When <c>true</c>, the request is sent as a oneway request. When <c>false</c>, the request is sent as
        /// a twoway request unless the operation is marked oneway in its Slice definition.</value>
        public bool IsOneway { get; set; }
    }
}
