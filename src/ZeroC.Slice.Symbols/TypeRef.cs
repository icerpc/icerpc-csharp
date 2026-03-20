// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a reference to a Slice type.</summary>
public readonly record struct TypeRef
{
    /// <summary>Gets the type this reference points to.</summary>
    public IType Type { get; init; }

    /// <summary>Gets the list of attributes for the reference.</summary>
    public ImmutableList<Attribute> Attributes { get; init; }
}
