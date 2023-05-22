// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Internal;

/// <summary>The Ice protocol class.</summary>
internal sealed class IceProtocol : Protocol
{
    /// <summary>Gets the Ice protocol singleton.</summary>
    internal static IceProtocol Instance { get; } = new();

    /// <summary>Checks if this absolute path is well-formed.</summary>
    /// <remarks>This check is more lenient than the check performed when encoding a service address with Slice1 because
    /// we want the default path (`/`) to be a valid path for all protocols. Sending a request to a null/empty identity
    /// is in itself ok and will most likely result in a dispatch exception with a
    /// <see cref="StatusCode.ServiceNotFound" /> status code.</remarks>
    internal override void CheckPath(string uriPath)
    {
        string workingPath = uriPath[1..]; // removes leading /.
        int firstSlash = workingPath.IndexOf('/', StringComparison.Ordinal);

        // We can have at most one slash in the working path.
        if (firstSlash != -1 && firstSlash != workingPath.LastIndexOf('/'))
        {
            throw new FormatException($"Too many slashes in path '{uriPath}'.");
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
                    throw new FormatException("The value of the adapter-id parameter cannot be empty.");
                }
            }
            else
            {
                throw new FormatException($"Invalid ice service address parameter name '{name}'.");
            }
        }
    }

    private IceProtocol()
        : base(
            name: "ice",
            defaultPort: 4061,
            hasFields: false,
            hasFragment: true,
            hasPayloadContinuation: false,
            supportsPayloadWriterInterceptors: false,
            byteValue: 1)
    {
    }
}
