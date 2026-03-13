// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a dictionary type in Slice.
/// </summary>
public record class DictionaryType : Symbol
{
    /// <summary>
    /// Gets the dictionary's key type.
    /// </summary>
    public required TypeRef KeyType { get; init; }

    /// <summary>
    /// Gets the dictionary's value type.
    /// </summary>
    public required TypeRef ValueType { get; init; }
}
