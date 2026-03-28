// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a diagnostic message produced during code generation.</summary>
public readonly record struct Diagnostic
{
    /// <summary>Gets the severity level of this diagnostic.</summary>
    public required DiagnosticLevel Level { get; init; }

    /// <summary>Gets the diagnostic message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the source location associated with this diagnostic, if any.</summary>
    public string? Source { get; init; }
}
