// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents an interface type defined in Slice.
/// </summary>
public record class Interface : Symbol
{
    /// <summary>
    /// Gets the interface's entity information.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets the list of base interfaces for this interface.
    /// </summary>
    public required ImmutableList<Interface> Bases { get; init; }

    /// <summary>
    /// Gets the list of operations defined in this interface.
    /// </summary>
    public required ImmutableList<Operation> Operations { get; init; }
}
