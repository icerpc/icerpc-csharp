// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>The common interface of all traits, and all Slice entities that can implement traits.</summary>
    public interface ITrait
    {
        /// <summary>Encodes the trait to the provided <see cref="IceEncoder"/>.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        void EncodeTrait(ref IceEncoder encoder);
    }
}
