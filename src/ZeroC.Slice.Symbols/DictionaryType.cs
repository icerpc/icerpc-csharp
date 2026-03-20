// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a dictionary type in Slice.</summary>
public class DictionaryType : ISymbol, IType
{
    /// <summary>Gets the dictionary's key type.</summary>
    public required TypeRef KeyType { get; init; }

    /// <summary>Gets the dictionary's value type.</summary>
    public required TypeRef ValueType { get; init; }

    /// <summary>Gets a value indicating whether the value type is optional.</summary>
    public required bool ValueTypeIsOptional { get; init; }
}
