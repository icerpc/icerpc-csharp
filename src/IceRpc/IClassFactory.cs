// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A class factory is responsible for creating class and remote exception instances from Ice type IDs
    /// or compact IDs. Used by <see cref="Ice11Decoder"/>.</summary>
    public interface IClassFactory
    {
        /// <summary>Creates a class or exception instance for the given type ID or compact ID.</summary>
        /// <param name="typeId">The type ID or compact ID.</param>
        /// <returns>A new instance or null if the factory doesn't know the type ID/compact ID.</returns>
        object? CreateClass(string typeId);
    }
}
