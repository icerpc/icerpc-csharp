// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>The result of a code generation transform: generated files and any diagnostics.</summary>
public readonly record struct GeneratorResponse
{
    /// <summary>Gets the list of generated files.</summary>
    public required IList<GeneratedFile> GeneratedFiles { get; init; }

    /// <summary>Gets the list of diagnostics produced during generation.</summary>
    public required IList<Diagnostic> Diagnostics { get; init; }
}
