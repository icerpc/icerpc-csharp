// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal;

/// <summary>The IceRPC protocol class.</summary>
internal sealed class IceRpcProtocol : Protocol
{
    /// <summary>Gets the IceRpc protocol singleton.</summary>
    internal static IceRpcProtocol Instance { get; } = new();

    private IceRpcProtocol()
        : base(
            name: "icerpc",
            defaultPort: 4062,
            hasFields: true,
            hasFragment: false,
            supportsPayloadContinuation: true,
            byteValue: 2)
    {
    }
}
