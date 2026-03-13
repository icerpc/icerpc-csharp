// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a Slice enumeration with fields, mapped to a Dunet discriminated union.
/// </summary>
public record class EnumWithFields : Symbol
{
    /// <summary>
    /// Gets the enum's entity information.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets a value indicating whether this enumeration is a compact enumeration. Compact enumerations cannot include
    /// unknown enumerators.
    /// </summary>
    public required bool IsCompact { get; init; }

    /// <summary>
    /// Gets a value indicating whether this enumeration is unchecked.
    /// </summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>
    /// Gets the list of enumerators for this enumeration.
    /// </summary>
    public required ImmutableList<Enumerator> Enumerators { get; init; }

    /// <summary>
    /// Represents an enumerator in a Slice enumeration with fields.
    /// </summary>
    public record class Enumerator
    {
        /// <summary>
        /// Gets the enumerator's entity information.
        /// </summary>
        public required EntityInfo EntityInfo { get; init; }

        /// <summary>
        /// Gets the list of fields for this enumerator.
        /// </summary>
        public required ImmutableList<Field> Fields { get; init; }
    }
}
