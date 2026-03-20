// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents an operation defined in a Slice interface.</summary>
public class Operation : Entity
{
    /// <summary>Gets a value indicating whether this operation is idempotent.</summary>
    public required bool IsIdempotent { get; init; }

    /// <summary>Gets the list of parameters for this operation.</summary>
    public required ImmutableList<Field> Parameters { get; init; }

    /// <summary>Gets a value indicating whether this operation has a streamed parameter. When <see langword="true"/>,
    /// the last parameter in the <see cref="Parameters"/> list is a streamed parameter.</summary>
    public required bool HasStreamedParameter { get; init; }

    /// <summary>Gets the list of return types for this operation.</summary>
    public required ImmutableList<Field> ReturnType { get; init; }

    /// <summary>Gets a value indicating whether this operation has a streamed return. When <see langword="true"/>, the
    /// last return type in the <see cref="ReturnType"/> list is a streamed return.</summary> 
    public required bool HasStreamedReturn { get; init; }
}
