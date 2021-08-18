// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A class factory is responsible for creating class and exception instances from Ice type IDs and
    /// compact IDs.</summary>
    public interface IClassFactory
    {
        /// <summary>Creates a class or exception instance for the given type ID or compact ID.</summary>
        /// <param name="typeId">The type ID or compact ID.</param>
        /// <returns>A new instance or null if the factory doesn't know the type ID/compact ID.</returns>
        object? Create(string typeId);

        /// <summary>Creates a remote exception instance for the given type ID.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="origin">The remote exception origin.</param>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>A new instance or null if the factory doesn't know the type ID.</returns>
        RemoteException? CreateRemoteException(
            string typeId,
            string message,
            RemoteExceptionOrigin origin,
            Ice20Decoder decoder);
    }
}
