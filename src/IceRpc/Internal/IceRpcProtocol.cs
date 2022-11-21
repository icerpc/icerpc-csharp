// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

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
            byteValue: 2)
    {
    }
}
