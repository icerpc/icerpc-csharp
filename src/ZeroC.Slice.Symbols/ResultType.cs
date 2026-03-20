// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents the Slice built-in result type.</summary>
public class ResultType : ISymbol, IType
{
    /// <summary>Gets the type of the success value in the result.</summary>
    public required TypeRef SuccessType { get; init; }

    /// <summary>Gets the type of the failure value in the result.</summary>
    public required TypeRef FailureType { get; init; }

    /// <summary>Gets a value indicating whether the success type is optional.</summary>
    public required bool SuccessTypeIsOptional { get; init; }

    /// <summary>Gets a value indicating whether the failure type is optional.</summary>
    public required bool FailureTypeIsOptional { get; init; }
}
