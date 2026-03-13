// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a Slice enumeration with an underlying type, mapped to a C# enum.
/// </summary>
public record class EnumWithUnderlying : Symbol
{
    /// <summary>
    /// Gets the enum's entity information.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets a value indicating whether this enumeration is unchecked.
    /// </summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>
    /// Gets the underlying type of this enumeration.
    /// </summary>
    public required Builtin Underlying { get; init; }

    /// <summary>
    /// Gets the list of enumerators for this enumeration.
    /// </summary>
    public required ImmutableList<Enumerator> Enumerators { get; init; }

    /// <summary>
    /// Represents an enumerator in a Slice enumeration with an underlying type.
    /// </summary>
    public record class Enumerator
    {
        /// <summary>
        /// Gets the enumerator's entity information.
        /// </summary>
        public required EntityInfo EntityInfo { get; init; }

        /// <summary>
        /// Gets the absolute value of this enumerator.
        /// </summary>
        public required ulong AbsoluteValue { get; init; }

        /// <summary>
        /// Gets a value indicating whether this enumerator is positive.
        /// </summary>
        public required bool IsPositive { get; init; }
    }
}
