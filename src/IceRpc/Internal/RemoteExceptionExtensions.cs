// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Extension methods for class <see cref="RemoteException"/>.</summary>
    internal static class RemoteExceptionExtensions
    {
        internal static bool IsIce1SystemException(this RemoteException remoteException) =>
            remoteException is ServiceNotFoundException ||
            remoteException is OperationNotFoundException ||
            remoteException is UnhandledException;
    }
}
