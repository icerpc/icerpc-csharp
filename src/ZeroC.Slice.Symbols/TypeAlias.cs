// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a type alias defined in Slice.
/// </summary>
public record class TypeAlias : Symbol
{
    /// <summary>
    /// Gets the entity information for this type alias.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets the type reference associated with this type alias.
    /// </summary>
    public required TypeRef UnderlyingType { get; init; }
}
