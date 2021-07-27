﻿// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A class factory is responsible for creating class an exception instances from IceRPC type IDs and
    /// compact type IDs.</summary>
    public interface IClassFactory
    {
        /// <summary>Creates a class instance for the given type ID.</summary>
        /// <param name="typeId">The class type ID</param>
        /// <returns>A new instance or null if the factory doesn't know the type ID.</returns>
        AnyClass? CreateClassInstance(string typeId);

        /// <summary>Creates a class instance for the given compact ID.</summary>
        /// <param name="compactId">The class compact ID</param>
        /// <returns>A new instance or null if the factory doesn't know the compact ID.</returns>
        AnyClass? CreateClassInstance(int compactId);

        /// <summary>Creates a remote exception instance for the given type ID.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        /// <param name="origin">The remote exception origin.</param>
        /// <param name="message">The exception message.</param>
        /// <returns>A new instance or null if the factory doesn't know the type ID.</returns>
        RemoteException? CreateRemoteException(string typeId, string? message, RemoteExceptionOrigin origin);
    }
}
