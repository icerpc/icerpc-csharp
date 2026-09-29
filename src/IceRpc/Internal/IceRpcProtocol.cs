// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Internal;

/// <summary>The IceRPC protocol class.</summary>
internal sealed class IceRpcProtocol : Protocol
{
    /// <summary>Gets the IceRpc protocol singleton.</summary>
    internal static IceRpcProtocol Instance { get; } = new();

    /// <summary>Checks if the server address parameters are valid. An icerpc server address has no parameters.
    /// </summary>
    internal override void CheckServerAddressParams(ImmutableDictionary<string, string> serverAddressParams)
    {
        if (serverAddressParams.Count > 0)
        {
            throw new FormatException("An icerpc server address cannot have parameters.");
        }
    }

    private IceRpcProtocol()
        : base(
            name: "icerpc",
            defaultPort: 4062,
            hasFields: true,
            hasPayloadContinuation: true,
            supportsPayloadWriterInterceptors: true,
            byteValue: 2)
    {
    }
}
