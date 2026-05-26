// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a diagnostic message produced during code generation.</summary>
public record struct Diagnostic
{
    /// <summary>Gets the exact kind of diagnostic.</summary>
    internal readonly Compiler.DiagnosticKind Kind { get; init; }

    /// <summary>Gets the source location associated with this diagnostic, if any.</summary>
    internal readonly string? Source { get; init; }

    /// <summary>Gets any notes that should be reported along with this diagnostic.</summary>
    internal IList<Compiler.DiagnosticNote> Notes { get; init; }

    /// <summary>Creates a new diagnostic to describe an attribute which was invalidly applied to an element.</summary>
    public static Diagnostic InvalidAttribute(string directive, string source) => new()
    {
        Kind = new Compiler.DiagnosticKind.InvalidAttribute(directive),
        Source = source,
        Notes = [],
    };

    /// <summary>Creates a new diagnostic to describe an unknown attribute.</summary>
    public static Diagnostic UnknownAttribute(string directive, string source) => new()
    {
        Kind = new Compiler.DiagnosticKind.UnknownAttribute(directive),
        Source = source,
        Notes = [],
    };

    /// <summary>Creates a new diagnostic to describe a required attribute which was not present.</summary>
    public static Diagnostic MissingRequiredAttribute(string expectedAttribute, string source) => new()
    {
        Kind = new Compiler.DiagnosticKind.MissingRequiredAttribute(expectedAttribute),
        Source = source,
        Notes = [],
    };

    /// <summary>Creates a new diagnostic to describe an attribute with an incorrect number of arguments.</summary>
    public static Diagnostic IncorrectAttributeArgumentCount(string directive, int expected, int actual, string source)
    {
        byte exp = expected > byte.MaxValue ? byte.MaxValue : (byte)expected;
        byte act = actual > byte.MaxValue ? byte.MaxValue : (byte)actual;
        return new()
        {
            Kind = new Compiler.DiagnosticKind.IncorrectAttributeArgumentCount(directive, exp, exp, act),
            Source = source,
            Notes = [],
        };
    }

    /// <summary>Returns whether this diagnostic represents an error.</summary>
    public bool IsError()
        => Kind is not Compiler.DiagnosticKind.Info and not Compiler.DiagnosticKind.Warning;

    /// <summary>Adds the provided message to this diagnostic as a note.</summary>
    public void AddNote(string message, string? source = null)
        => Notes.Add(new Compiler.DiagnosticNote(message, source));
}
