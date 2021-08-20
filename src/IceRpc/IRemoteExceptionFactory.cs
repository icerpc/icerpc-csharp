// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A remote exception factory creates remote exception instances from Ice type IDs. Used by
    /// <see cref="Ice20Decoder"/>.</summary>
    public interface IRemoteExceptionFactory
    {
        /// <summary>Creates a remote exception instance for the given type ID.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>A new instance or null if the factory doesn't know the type ID.</returns>
        RemoteException? CreateRemoteException(string typeId, Ice20Decoder decoder);
    }
}
