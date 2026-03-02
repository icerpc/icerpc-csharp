// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;

namespace IceRpc.Slice.Ice;

public partial record struct Identity
{
    /// <summary>Gets the null identity.</summary>
    internal static Identity Empty { get; } = new("", "");

    /// <summary>Parses a path into an identity, including the null/empty identity.</summary>
    /// <param name="path">The path (percent escaped).</param>
    /// <returns>The corresponding identity.</returns>
    public static Identity Parse(string path)
    {
        var iceIdentity = IceIdentity.Parse(path);
        return new(iceIdentity.Name, iceIdentity.Category);
    }

    /// <summary>Converts this identity into a path.</summary>
    /// <returns>The identity converted into a path string.</returns>
    public readonly string ToPath() => new IceIdentity(Name, Category).ToPath();

    /// <summary>Converts this identity into a path string.</summary>
    /// <returns>The identity converted into a path string.</returns>
    public override readonly string ToString() => ToPath();
}
