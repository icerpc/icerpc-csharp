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
            encoder.EncodeString(Uri.UnescapeDataString(fragment));
        }
    }

    internal static string[] DecodeFacet(this ref IceDecoder decoder) =>
        decoder.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeString());

    internal static string ToFragment(this IList<string> facet) =>
        facet.Count switch
        {
            0 => "",
            1 => Uri.EscapeDataString(facet[0]),
            _ => throw new InvalidDataException("Received a Facet with too many sequence elements.")
        };

    internal static IList<string> ToFacet(this string fragment) =>
        fragment.Length == 0 ? [] : new List<string> { Uri.UnescapeDataString(fragment) };
}
