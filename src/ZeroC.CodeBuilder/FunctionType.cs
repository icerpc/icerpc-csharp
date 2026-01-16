// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>Specifies the type of function body.</summary>
public enum FunctionType
{
    /// <summary>A function declaration without a body (ends with semicolon).</summary>
    Declaration,

    /// <summary>A function with a block body (using braces).</summary>
    BlockBody,

    /// <summary>A function with an expression body (using =>).</summary>
    ExpressionBody,
}
