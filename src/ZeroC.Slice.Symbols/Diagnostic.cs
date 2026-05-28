// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a diagnostic message produced during code generation.</summary>
public readonly record struct Diagnostic
{
    /// <summary>Returns whether this diagnostic represents an error.</summary>
    public bool IsError => Kind is not Compiler.DiagnosticKind.Info and not Compiler.DiagnosticKind.Warning;

    /// <summary>Gets the exact kind of diagnostic.</summary>
    private Compiler.DiagnosticKind Kind { get; }

    /// <summary>Gets the source location associated with this diagnostic, if any.</summary>
    private string? Source { get; }

    /// <summary>Gets any notes that should be reported along with this diagnostic.</summary>
    private IList<Compiler.DiagnosticNote> Notes { get; }

    private Diagnostic(Compiler.DiagnosticKind kind, string? source)
    {
        Kind = kind;
        Source = source;
        Notes = [];
    }

    /// <summary>Creates a new diagnostic to describe a general error.</summary>
    public static Diagnostic Error(string message, string? source = null) =>
        new(new Compiler.DiagnosticKind.Error(message), source);

    /// <summary>Creates a new diagnostic to describe an attribute which was invalidly applied to an element.</summary>
    public static Diagnostic InvalidAttribute(string directive, string source) =>
        new(new Compiler.DiagnosticKind.InvalidAttribute(directive), source);

    /// <summary>Creates a new diagnostic to describe an unknown attribute.</summary>
    public static Diagnostic UnknownAttribute(string directive, string source) =>
        new(new Compiler.DiagnosticKind.UnknownAttribute(directive), source);

    /// <summary>Creates a new diagnostic to describe a required attribute which was not present.</summary>
    public static Diagnostic MissingRequiredAttribute(string expectedAttribute, string source) =>
        new(new Compiler.DiagnosticKind.MissingRequiredAttribute(expectedAttribute), source);

    /// <summary>Creates a new diagnostic to describe an attribute with an incorrect number of arguments.</summary>
    public static Diagnostic IncorrectAttributeArgumentCount(string directive, int expected, int actual, string source)
    {
        byte exp = expected > byte.MaxValue ? byte.MaxValue : (byte)expected;
        byte act = actual > byte.MaxValue ? byte.MaxValue : (byte)actual;
        return new(new Compiler.DiagnosticKind.IncorrectAttributeArgumentCount(directive, exp, exp, act), source);
    }

    /// <summary>Adds the provided message to this diagnostic as a note.</summary>
    public void AddNote(string message, string? source = null) =>
        Notes.Add(new Compiler.DiagnosticNote(message, source));

    /// <summary>Converts this diagnostic into a form that can be transmitted to the Slice compiler.</summary>
    internal Compiler.Diagnostic ToCompilerDiagnostic() => new Compiler.Diagnostic(Kind, Source, Notes);
}
