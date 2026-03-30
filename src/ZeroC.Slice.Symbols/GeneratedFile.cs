// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a generated output file.</summary>
public readonly record struct GeneratedFile
{
    /// <summary>Gets the path of the generated file.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the contents of the generated file.</summary>
    public required string Contents { get; init; }
}
