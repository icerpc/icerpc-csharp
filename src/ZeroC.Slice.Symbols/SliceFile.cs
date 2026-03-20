// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a Slice file.</summary>
public class SliceFile
{
    /// <summary>Gets the path of the Slice file.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the module defined in the Slice file.</summary>
    public required Module Module { get; init; }

    /// <summary>Gets the list of file-level attributes defined in the Slice file.</summary>
    public required ImmutableList<Attribute> Attributes { get; init; }

    /// <summary>Gets the list of symbols defined in the Slice file.</summary>
    public required ImmutableList<ISymbol> Contents { get; init; }
}
