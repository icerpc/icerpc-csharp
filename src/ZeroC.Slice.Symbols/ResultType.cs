// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents the Slice built-in result type.
/// </summary>
public record class ResultType : Symbol
{
    /// <summary>
    /// Gets the type of the success value in the result.
    /// </summary>
    public required TypeRef SuccessType { get; init; }

    /// <summary>
    /// Gets the type of the failure value in the result.
    /// </summary>
    public required TypeRef FailureType { get; init; }
}
