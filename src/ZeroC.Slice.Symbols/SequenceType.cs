// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a sequence type defined in Slice.</summary>
public class SequenceType : ISymbol, IType
{
    /// <summary>Gets the sequence element type.</summary>
    public required TypeRef ElementType { get; init; }

    /// <summary>Gets a value indicating whether the element type is optional.</summary>
    public required bool ElementTypeIsOptional { get; init; }
}
