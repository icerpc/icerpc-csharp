// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;

namespace IceRpc.Slice
{
    /// <summary>Provides an extension method for class FeatureCollection, to get and set specific features.</summary>
    public static class FeatureCollectionExtensions
    {
        /// <summary>Gets the value for the Slice decoder's maximum depth from a feature collection.</summary>
        /// <param name="features">The feature collection.</param>
        /// <returns>The maximum depth if found in features, otherwise -1.</returns>
        public static int GetSliceDecoderMaxDepth(this FeatureCollection features) =>
           features.Get<SliceDecoderMaxDepth>()?.Value ?? -1;

        /// <summary>Sets the value for the Slice decoder's maximum depth in a feature collection.</summary>
        /// <param name="features">The source feature collection.</param>
        /// <param name="value">The new value for the decoder's maximum depth.</param>
        /// <returns>The new or updated feature collection.</returns>
        public static FeatureCollection WithSliceDecoderMaxDepth(this FeatureCollection features, int value) =>
           features.With(new SliceDecoderMaxDepth { Value = value });
    }
}
