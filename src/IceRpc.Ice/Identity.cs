// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;

namespace IceRpc.Ice;

public partial record struct Identity
{
    /// <summary>Gets the null identity.</summary>
    internal static Identity Empty { get; } = new("", "");

    /// <summary>Parses a path into an identity, including the null identity.</summary>
    /// <param name="path">The path of the identity, as returned by <see cref="ToPath" />: <c>/</c> for the null
    /// identity, <c>/name</c>, or <c>/category/name</c>.</param>
    /// <returns>The corresponding identity.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="path" /> is not the path of an identity. A
    /// segment must not be empty, and must percent-encode exactly the characters that are not valid in a URI path
    /// segment, as UTF-8 bytes with uppercase hex digits. For example, <c>/a:b</c> is the path of the identity with
    /// name <c>a:b</c>, while <c>/a%3Ab</c> is not a valid path.</exception>
    public static Identity Parse(string path)
    {
        var iceIdentity = IceIdentity.Parse(path);
        return new(iceIdentity.Name, iceIdentity.Category);
    }

    /// <summary>Converts this identity into a path.</summary>
    /// <returns><c>/</c> for the null identity, otherwise <c>/name</c> or <c>/category/name</c>. Each segment
    /// percent-encodes the UTF-8 bytes of the characters that are not valid in a URI path segment, with uppercase hex
    /// digits, and leaves all other characters unescaped.</returns>
    public readonly string ToPath() => new IceIdentity(Name, Category).ToPath();

    /// <summary>Converts this identity into a path string.</summary>
    /// <returns>The identity converted into a path string.</returns>
    public override readonly string ToString() => ToPath();
}
