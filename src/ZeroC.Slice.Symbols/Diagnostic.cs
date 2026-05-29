// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a diagnostic message produced during code generation.</summary>
public readonly record struct Diagnostic
{
    /// <summary>Returns whether this diagnostic represents an error.</summary>
    public bool IsError => _kind is not Compiler.DiagnosticKind.Info and not Compiler.DiagnosticKind.Warning;

    /// <summary>The exact kind of diagnostic.</summary>
    private readonly Compiler.DiagnosticKind _kind;

    /// <summary>The source location associated with this diagnostic, if any.</summary>
    private readonly string? _source;

    /// <summary>Notes that should be reported along with this diagnostic.</summary>
    private readonly IList<Compiler.DiagnosticNote> _notes;

    /// <summary>Creates a new diagnostic to describe a general error.</summary>
    /// <param name="message">A message describing the error.</param>
    /// <param name="source">The source location associated with this diagnostic, if any.</param>
    /// <returns>A new diagnostic.</returns>
    public static Diagnostic Error(string message, string? source = null) =>
        new(new Compiler.DiagnosticKind.Error(message), source);

    /// <summary>Creates a new diagnostic to describe an attribute which was invalidly applied to an element.</summary>
    /// <param name="directive">The directive of the attribute that was invalid.</param>
    /// <param name="source">The source location of the invalid attribute.</param>
    /// <returns>A new diagnostic.</returns>
    public static Diagnostic InvalidAttribute(string directive, string source) =>
        new(new Compiler.DiagnosticKind.InvalidAttribute(directive), source);

    /// <summary>Creates a new diagnostic to describe an unknown attribute.</summary>
    /// <param name="directive">The directive of the attribute that was unknown.</param>
    /// <param name="source">The source location of the unknown attribute.</param>
    /// <returns>A new diagnostic.</returns>
    public static Diagnostic UnknownAttribute(string directive, string source) =>
        new(new Compiler.DiagnosticKind.UnknownAttribute(directive), source);

    /// <summary>Creates a new diagnostic to describe a required attribute which was not present.</summary>
    /// <param name="expectedAttribute">An example of the attribute that was required but missing.</param>
    /// <param name="source">The source location of the Slice construct that was missing the required attribute.</param>
    /// <returns>A new diagnostic.</returns>
    public static Diagnostic MissingRequiredAttribute(string expectedAttribute, string source) =>
        new(new Compiler.DiagnosticKind.MissingRequiredAttribute(expectedAttribute), source);

    /// <summary>Creates a new diagnostic to describe an attribute with an incorrect number of arguments.</summary>
    /// <param name="directive">The directive of the attribute that had an incorrect number of arguments.</param>
    /// <param name="expected">The correct number of arguments that should have been supplied to this attribute.</param>
    /// <param name="actual">The actual number of arguments that were supplied to this attribute.</param>
    /// <param name="source">The source location of the attribute which with an incorrect number of arguments.</param>
    /// <returns>A new diagnostic.</returns>
    public static Diagnostic IncorrectAttributeArgumentCount(string directive, int expected, int actual, string source)
    {
        byte exp = expected > byte.MaxValue ? byte.MaxValue : (byte)expected;
        byte act = actual > byte.MaxValue ? byte.MaxValue : (byte)actual;
        return new(new Compiler.DiagnosticKind.IncorrectAttributeArgumentCount(directive, exp, exp, act), source);
    }

    /// <summary>Adds the provided message to this diagnostic as a note.</summary>
    /// <param name="message">The message to add as a note.</param>
    /// <param name="source">The source location associated with this note, if any.</param>
    public void AddNote(string message, string? source = null) =>
        _notes.Add(new Compiler.DiagnosticNote(message, source));

    /// <summary>Converts this diagnostic into a form that can be transmitted to the Slice compiler.</summary>
    internal Compiler.Diagnostic ToCompilerDiagnostic() => new Compiler.Diagnostic(_kind, _source, _notes);

    private Diagnostic(Compiler.DiagnosticKind kind, string? source)
    {
        _kind = kind;
        _source = source;
        _notes = [];
    }
}
