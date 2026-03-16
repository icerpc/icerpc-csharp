// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a module defined in Slice.</summary>
public record class Module
{
    /// <summary>Gets the module's identifier.</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the module's attributes.</summary>
    public required ImmutableList<Attribute> Attributes { get; init; }
}
