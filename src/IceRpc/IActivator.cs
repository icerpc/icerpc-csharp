// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Ice decoders use activators to create instances of classes and remote exceptions from Ice type IDs.
    /// </summary>
    public interface IActivator<T> where T : IceDecoder
    {
        /// <summary>Creates an instance of a class or remote exception based on a type ID.</summary>
        /// <param name="typeId">The Ice type ID.</param>
        /// <param name="decoder">The decoder.</param>
        /// <returns>A new instance of the type identified by <paramref name="typeId"/>. This instance may be fully
        /// decoded using decoder, or only partially decoded.</returns>
        object? CreateInstance(string typeId, T decoder);
    }
}
