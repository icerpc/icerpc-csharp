// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;

namespace IceRpc.Slice
{
    /// <summary>Provides an extension method for class FeatureCollection, to get and set specific features.</summary>
    public static class FeatureCollectionExtensions
    {
        /// <summary>Gets the value for the Slice decoder's class graph max depth from a feature collection.</summary>
        /// <param name="features">The feature collection.</param>
        /// <returns>The class graph max depth if found in features, otherwise -1.</returns>
        public static int GetClassGraphMaxDepth(this FeatureCollection features) =>
           features.Get<ClassGraphMaxDepth>()?.Value ?? -1;

        /// <summary>Sets the value for the Slice decoder's class graph max depth in a feature collection.</summary>
        /// <param name="features">The source feature collection.</param>
        /// <param name="value">The new value for class graph max depth.</param>
        /// <returns>The new or updated feature collection.</returns>
        public static FeatureCollection WithClassGraphMaxDepth(this FeatureCollection features, int value) =>
           features.With(new ClassGraphMaxDepth { Value = value });
    }
}
