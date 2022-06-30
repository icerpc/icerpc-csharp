// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Collections.Immutable;

namespace IceRpc.Internal;

/// <summary>The Ice protocol class.</summary>
internal sealed class IceProtocol : Protocol
{
    public override int DefaultUriPort => 4061;

    public override bool HasFragment => true;

    public override bool IsSupported => true;

    /// <summary>Gets the Ice protocol singleton.</summary>
    internal static IceProtocol Instance { get; } = new();

    internal override SliceEncoding SliceEncoding => SliceEncoding.Slice1;

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

    private IceProtocol()
        : base(IceName)
    {
    }
}
