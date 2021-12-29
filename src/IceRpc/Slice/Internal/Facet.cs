// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice.Internal
{
    // Extends the (temporary) Slice-defined Facet struct.
    internal readonly partial record struct Facet
    {
        public override string ToString() => Value.Count == 0 ? "" : Value[0];

        internal static Facet FromString(string s) =>
            new(s.Length > 0 ? ImmutableList.Create(s) : ImmutableList<string>.Empty);

        internal void CheckValue()
        {
            if (Value.Count > 1)
            {
                throw new InvalidDataException($"invalid facet sequence with {Value.Count} elements");
            }
        }
    }
}
