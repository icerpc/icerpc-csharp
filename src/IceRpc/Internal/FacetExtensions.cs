// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;

namespace IceRpc.Internal;

/// <summary>Provides extension methods for Ice facets.</summary>
internal static class FacetExtensions
{
    internal static void CheckFacetCount(this IList<string> facet)
    {
        if (facet.Count > 1)
        {
            throw new InvalidDataException("Received a Facet with too many sequence elements.");
        }
    }

    internal static void EncodeFragmentAsFacet(this ref IceEncoder encoder, string fragment)
    {
        if (fragment.Length == 0)
        {
            encoder.EncodeSize(0);
        }
        else
        {
            encoder.EncodeSize(1);
            encoder.EncodeString(DecodeFragment(fragment));
        }
    }

    internal static string[] DecodeFacet(this ref IceDecoder decoder) =>
        decoder.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeString());

    /// <summary>Converts a facet into a URI fragment, percent-encoded in canonical form.</summary>
    internal static string ToFragment(this IList<string> facet) =>
        facet.Count switch
        {
            0 => "",
            1 => PercentEncoding.Encode(facet[0], PercentEncoding.FragmentChars),
            _ => throw new InvalidDataException("Received a Facet with too many sequence elements.")
        };

    /// <summary>Converts a URI fragment into a facet.</summary>
    /// <exception cref="FormatException">Thrown when <paramref name="fragment" /> is not percent-encoded in the
    /// canonical form produced by <see cref="ToFragment" />.</exception>
    internal static IList<string> ToFacet(this string fragment) =>
        fragment.Length == 0 ? [] : new List<string> { DecodeFragment(fragment) };

    private static string DecodeFragment(string fragment) =>
        PercentEncoding.TryDecode(fragment, PercentEncoding.FragmentChars, out string facet) ? facet :
            throw new FormatException(
                $"Invalid facet fragment '{fragment}': the fragment must percent-encode, with uppercase hex digits, the UTF-8 bytes of exactly the characters that are not valid in a URI fragment.");
}
