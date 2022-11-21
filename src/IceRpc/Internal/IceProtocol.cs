// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Internal;

/// <summary>The Ice protocol class.</summary>
internal sealed class IceProtocol : Protocol
{
    /// <summary>Gets the Ice protocol singleton.</summary>
    internal static IceProtocol Instance { get; } = new();

    /// <summary>Checks if this absolute path holds a valid identity.</summary>
    internal override void CheckPath(string uriPath)
    {
        string workingPath = uriPath[1..]; // removes leading /.
        int firstSlash = workingPath.IndexOf('/', StringComparison.Ordinal);

        string escapedName;

        if (firstSlash == -1)
        {
            escapedName = workingPath;
        }
        else
        {
            if (firstSlash != workingPath.LastIndexOf('/'))
            {
                throw new FormatException($"too many slashes in path '{uriPath}'");
            }
            escapedName = workingPath[(firstSlash + 1)..];
        }

        if (escapedName.Length == 0)
        {
            throw new FormatException($"invalid empty identity name in path '{uriPath}'");
        }
    }

    /// <summary>Checks if the service address parameters are valid. The only valid parameter is adapter-id with a
    /// non-empty value.</summary>
    internal override void CheckServiceAddressParams(ImmutableDictionary<string, string> serviceAddressParams)
    {
        foreach ((string name, string value) in serviceAddressParams)
        {
            if (name == "adapter-id")
            {
                if (value.Length == 0)
                {
                    throw new FormatException("the value of the adapter-id parameter cannot be empty");
                }
            }
            else
            {
                throw new FormatException($"'{name}' is not a valid ice service address parameter");
            }
        }
    }

    private IceProtocol()
        : base(
            name: "ice",
            defaultPort: 4061,
            hasFields: false,
            hasFragment: true,
            byteValue: 1)
    {
    }
}
