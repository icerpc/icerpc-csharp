// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a Slice enumeration with fields.</summary>
public class EnumWithFields : Entity, ISymbol, IType
{
    /// <summary>Gets a value indicating whether this enumeration is a compact enumeration. Compact enumerations cannot
    /// include unknown enumerators.</summary>
    public required bool IsCompact { get; init; }

    /// <summary>Gets a value indicating whether this enumeration is unchecked.</summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>Gets the list of enumerators for this enumeration.</summary>
    public required ImmutableList<Enumerator> Enumerators { get; init; }

    /// <summary>Represents an enumerator in a Slice enumeration with fields.</summary>
    public class Enumerator : Entity
    {
        /// <summary>Gets the discriminant value used for encoding/decoding this enumerator.</summary>
        public required int Discriminant { get; init; }

        /// <summary>Gets the list of fields for this enumerator.</summary>
        public required ImmutableList<Field> Fields { get; init; }
    }
}
