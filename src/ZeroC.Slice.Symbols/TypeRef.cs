// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a reference to a Slice type.</summary>
public class TypeRef
{
    /// <summary>Gets the type this reference points to.</summary>
    public required IType Type { get; init; }

    /// <summary>Gets the list of attributes for the reference.</summary>
    public required ImmutableList<Attribute> Attributes { get; init; }
}
