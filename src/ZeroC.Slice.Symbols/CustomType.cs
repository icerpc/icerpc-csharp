// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a custom type defined in Slice, where the user defines the target language mapped type and provides the
/// encode and decode methods.
/// </summary>
public record class CustomType : Symbol
{
    /// <summary>
    /// Gets the entity info for this custom type.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }
}
