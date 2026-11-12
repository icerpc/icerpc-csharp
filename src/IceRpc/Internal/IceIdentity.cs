// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal readonly partial record struct IceIdentity
{
    /// <summary>Converts this identity into a path string.</summary>
    /// <returns>The identity converted into a path string.</returns>
    public override readonly string ToString() => ToPath();

    /// <summary>Parses a path into an identity, including the null identity.</summary>
    /// <param name="path">The path: <c>/</c> for the null identity, <c>/name</c>, or <c>/category/name</c>, with each
    /// segment percent-encoded in the canonical form produced by <see cref="ToPath" />.</param>
    /// <returns>The corresponding identity.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="path" /> is not the path of an identity.
    /// </exception>
    internal static IceIdentity Parse(string path)
    {
        if (path == "/")
        {
            return new("", "");
        }

        if (path.Length == 0 || path[0] != '/')
        {
            throw new FormatException($"Invalid identity path '{path}': the path must start with '/'.");
        }

        ReadOnlySpan<char> segments = path.AsSpan(1);
        int slash = segments.IndexOf('/');
        ReadOnlySpan<char> categorySegment = slash == -1 ? [] : segments[..slash];
        ReadOnlySpan<char> nameSegment = slash == -1 ? segments : segments[(slash + 1)..];

        if (nameSegment.Contains('/'))
        {
            throw new FormatException(
                $"Invalid identity path '{path}': the path must have the form '/name' or '/category/name'.");
        }

        if (nameSegment.IsEmpty || (slash != -1 && categorySegment.IsEmpty))
        {
            throw new FormatException($"Invalid identity path '{path}': the path contains an empty segment.");
        }

        if (!PercentEncoding.TryDecode(nameSegment, PercentEncoding.PathSegmentChars, out string name) ||
            !PercentEncoding.TryDecode(categorySegment, PercentEncoding.PathSegmentChars, out string category))
        {
            throw new FormatException(
                $"Invalid identity path '{path}': each segment must percent-encode, with uppercase hex digits, the UTF-8 bytes of exactly the characters that are not valid in a URI path segment.");
        }

        return new(name, category);
    }

    /// <summary>Converts this identity into a path.</summary>
    /// <returns><c>/</c> for the null identity, or <c>/name</c> or <c>/category/name</c>, with each segment
    /// percent-encoded in canonical form.</returns>
    internal readonly string ToPath()
    {
        if (Name.Length == 0)
        {
            return "/";
        }

        string name = PercentEncoding.Encode(Name, PercentEncoding.PathSegmentChars);
        return Category.Length > 0 ?
            $"/{PercentEncoding.Encode(Category, PercentEncoding.PathSegmentChars)}/{name}" :
            $"/{name}";
    }
}
