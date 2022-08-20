// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc;

/// <summary>Protocol identifies a RPC protocol supported by IceRPC.</summary>
public class Protocol
{
    /// <summary>Gets the <c>ice</c> protocol.</summary>
    public static Protocol Ice => IceProtocol.Instance;

    /// <summary>Gets the <c>icerpc</c> protocol.</summary>
    public static Protocol IceRpc => IceRpcProtocol.Instance;

    /// <summary>Gets the default port for this protocol.</summary>
    public ushort DefaultPort { get; }

    /// <summary>Gets a value indicating whether or not this protocol supports fields.</summary>
    /// <returns><c>true</c> if the protocol supports fields; otherwise, <c>false</c>.</returns>
    public bool HasFields { get; }

    /// <summary>Gets a value indicating whether or not this protocol supports fragments in service addresses.</summary>
    /// <returns><c>true</c> if the protocol supports fragments; otherwise, <c>false</c>.</returns>
    public bool HasFragment { get; }

    /// <summary>Gets the name of this protocol.</summary>
    public string Name { get; }

    /// <summary>Gets the byte value for this protocol, used as the "protocol major" with the Slice1 encoding.</summary>
    internal byte ByteValue { get; }

    /// <summary>Gets the Slice encoding that this protocol uses for its headers.</summary>
    /// <returns>The Slice encoding.</returns>
    internal SliceEncoding SliceEncoding { get; }

    /// <summary>Parses a string into a protocol.</summary>
    /// <param name="name">The name of the protocol.</param>
    /// <returns>A protocol with the given name in lowercase.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="name"/> is not ice or icerpc.</exception>
    public static Protocol Parse(string name) =>
        TryParse(name, out Protocol? protocol) ? protocol : throw new FormatException($"unknown protocol '{name}'");

    /// <summary>Tries to parse a string into a protocol.</summary>
    /// <param name="name">The name of the protocol.</param>
    /// <param name="protocol">The protocol parsed from the name.</param>
    /// <returns><c>true</c> when <paramref name="name"/> was successfully parsed into a protocol; otherwise,
    /// <c>false</c>.</returns>
    public static bool TryParse(string name, [NotNullWhen(true)] out Protocol? protocol)
    {
        name = name.ToLowerInvariant();
        protocol = name == IceRpc.Name ? IceRpc : (name == Ice.Name ? Ice : null);
        return protocol is not null;
    }

    /// <summary>Converts this protocol into a string.</summary>
    /// <returns>The name of the protocol.</returns>
    public override string ToString() => Name;

    internal static Protocol FromByteValue(byte value) =>
        value == Ice.ByteValue ? Ice :
            (value == IceRpc.ByteValue ? IceRpc :
                throw new NotSupportedException($"cannot convert '{value}' into a protocol"));

    /// <summary>Checks if a path is valid for this protocol.</summary>
    /// <param name="uriPath">The absolute path to check. The caller guarantees it's a valid URI absolute path.
    /// </param>
    /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
    internal virtual void CheckPath(string uriPath)
    {
        // by default, any URI absolute path is ok
    }

    /// <summary>Checks if these service address parameters are valid for this protocol.</summary>
    /// <param name="serviceAddressParams">The service address parameters to check.</param>
    /// <exception cref="FormatException">Thrown if the service address parameters are not valid.</exception>
    /// <remarks>This method does not and should not check if the parameter names and values are properly escaped;
    /// it does not check for the invalid empty and alt-server parameter names either.</remarks>
    internal virtual void CheckServiceAddressParams(ImmutableDictionary<string, string> serviceAddressParams)
    {
        // by default, any dictionary is ok
    }

    /// <summary>Constructs a protocol.</summary>
    private protected Protocol(
        string name,
        ushort defaultPort,
        bool hasFields,
        bool hasFragment,
        byte byteValue,
        SliceEncoding sliceEncoding)
    {
        Name = name;
        DefaultPort = defaultPort;
        HasFields = hasFields;
        HasFragment = hasFragment;
        ByteValue = byteValue;
        SliceEncoding = sliceEncoding;
    }
}
