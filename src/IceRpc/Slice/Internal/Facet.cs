// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice.Internal
{
    /// <summary>Represents the facet of a service with the Slice 1.1 encoding.</summary>
    internal readonly ref struct Facet
    {
        internal IList<string> Value { get; }

        public override string ToString() => Value.Count == 0 ? "" : Value[0];

        internal Facet(IList<string> value)
        {
            if (value.Count > 1)
            {
                throw new InvalidDataException($"invalid facet sequence with {value.Count} elements");
            }
            Value = value;
        }

        internal static Facet FromString(string s) =>
            new(s.Length > 0 ? ImmutableList.Create(s) : ImmutableList<string>.Empty);
    }
}
