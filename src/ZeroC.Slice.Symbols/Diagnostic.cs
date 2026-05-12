// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a diagnostic message produced during code generation.</summary>
public readonly record struct Diagnostic
{
    /// <summary>Gets the exact kind of diagnostic.</summary>
    internal Compiler.DiagnosticKind Kind { get; init; }

    /// <summary>Gets the source location associated with this diagnostic, if any.</summary>
    internal string? Source { get; init; }

    /// <summary>Gets whether this diagnostic represents an error.</summary>
    public bool IsError => Kind is not Compiler.DiagnosticKind.Info and not Compiler.DiagnosticKind.Warning;

    /// <summary>TODO</summary>
    public static Diagnostic InvalidAttribute(string directive, string source) => new()
    {
        Kind = new Compiler.DiagnosticKind.InvalidAttribute(directive),
        Source = source,
    };

    /// <summary>TODO</summary>
    public static Diagnostic UnknownAttribute(string directive, string source) => new()
    {
        Kind = new Compiler.DiagnosticKind.UnknownAttribute(directive),
        Source = source,
    };

    /// <summary>TODO</summary>
    public static Diagnostic MissingRequiredAttribute(string expectedAttribute, string source) => new()
    {
        Kind = new Compiler.DiagnosticKind.MissingRequiredAttribute(expectedAttribute),
        Source = source,
    };

    /// <summary>TODO</summary>
    public static Diagnostic IncorrectAttributeArgumentCount(string directive, int expected, int actual, string source)
    {
        byte exp = expected > byte.MaxValue ? byte.MaxValue : (byte)expected;
        byte act = actual > byte.MaxValue ? byte.MaxValue : (byte)actual;
        return new()
        {
            Kind = new Compiler.DiagnosticKind.IncorrectAttributeArgumentCount(directive, exp, exp, act),
            Source = source,
        };
    }
}
