// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a type alias defined in Slice.</summary>
public class TypeAlias : Entity, ISymbol, IType
{
    /// <summary>Gets the type reference associated with this type alias.</summary>
    public required TypeRef UnderlyingType { get; init; }
}
