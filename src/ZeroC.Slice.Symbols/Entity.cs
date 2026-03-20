// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a named entity defined in Slice.</summary>
public class Entity
{
    /// <summary>Gets the entity's identifier.</summary>
    public required string Identifier { get; init; }

    /// <summary>Gets the entity's attributes.</summary>
    public required ImmutableList<Attribute> Attributes { get; init; }

    /// <summary>Gets the module that contains this entity.</summary>
    public required Module Module { get; init; }

    /// <summary>Gets the doc comment associated with this entity, if any.</summary>
    public Comment? Comment { get; init; }

    /// <summary>Gets the fully scoped Slice identifier (e.g. "MyModule::MyType").</summary>
    public string ScopedIdentifier => $"{Module.Identifier}::{Identifier}";
}
