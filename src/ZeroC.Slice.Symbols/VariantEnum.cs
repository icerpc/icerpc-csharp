// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a Slice variant enumeration.</summary>
public class VariantEnum : Entity, ISymbol, IType
{
    /// <summary>Gets a value indicating whether this variant enumeration is a compact enumeration. Compact variant
    /// enumerations cannot include unknown variants.</summary>
    public required bool IsCompact { get; init; }

    /// <summary>Gets a value indicating whether this enumeration is unchecked.</summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>Gets the list of variants for this variant enumeration.</summary>
    public required ImmutableList<Variant> Variants { get; init; }

    /// <summary>Represents a variant in a Slice variant enumeration.</summary>
    public class Variant : Entity
    {
        /// <summary>Gets the discriminant value used for encoding/decoding this variant.</summary>
        public required int Discriminant { get; init; }

        /// <summary>Gets the list of fields for this variant.</summary>
        public required ImmutableList<Field> Fields { get; init; }
    }
}
