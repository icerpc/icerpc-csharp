// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

/// <summary>The Ice protocol class.</summary>
internal sealed class IceProtocol : Protocol
{
    /// <summary>Gets the Ice protocol singleton.</summary>
    internal static IceProtocol Instance { get; } = new();

    private IceProtocol()
        : base(
            name: "ice",
            defaultPort: 4061,
            hasFields: false,
            hasPayloadContinuation: false,
            supportsPayloadWriterInterceptors: false,
            byteValue: 1)
    {
    }
}
