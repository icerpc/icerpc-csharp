// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a struct defined in Slice.</summary>
public class Struct : Entity, ISymbol, IType
{
    /// <summary>Gets a value indicating whether this struct is compact.</summary>
    public required bool IsCompact { get; init; }

    /// <summary>Gets the list of fields defined in this struct.</summary>
    public required ImmutableList<Field> Fields { get; init; }
}
