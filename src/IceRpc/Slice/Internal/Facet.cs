// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice.Internal
{
    // Extends the (temporary) Slice-defined Facet struct.
    internal readonly partial record struct Facet
    {
        internal string ToFragment() => Value.Count == 0 ? "" : Uri.EscapeDataString(Value[0]);

        internal static Facet FromFragment(string fragment) => new(
            fragment.Length > 0 ?
                ImmutableList.Create(Uri.UnescapeDataString(fragment)) : ImmutableList<string>.Empty);

        internal void CheckValue()
        {
            if (Value.Count > 1)
            {
                throw new InvalidDataException($"invalid facet sequence with {Value.Count} elements");
            }
        }
    }
}
