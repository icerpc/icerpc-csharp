// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>The common interface of all Prx structs. It gives access to an untyped proxy object that can send
    /// requests to a remote IceRPC service.</summary>
    public interface IPrx
    {
        /// <summary>Gets the proxy object, or sets this proxy object during initialization.</summary>
        Proxy Proxy { get; init; }
    }
}
