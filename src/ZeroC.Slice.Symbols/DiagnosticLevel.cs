// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>The severity level of a diagnostic.</summary>
public enum DiagnosticLevel : byte
{
    /// <summary>An informational message.</summary>
    Info = 0,

    /// <summary>A warning message.</summary>
    Warning = 1,

    /// <summary>An error message.</summary>
    Error = 2,
}
