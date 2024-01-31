// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>A discriminated union that represents either a success or a failure. It is typically used as the return
/// type of Slice operations.</summary>
/// <typeparam name="TSuccess">The success type.</typeparam>
/// <typeparam name="TFailure">The failure type.</typeparam>
/// <remarks>The Slice Result type (a built-in generic type) maps to this generic class in C#. The Dunet source
/// generator provides many methods (Match, MatchSuccess, MatchFailure, MatchAsync, UnwrapSuccess, UnwrapFailure and
/// more) for this generic class. See <see href="https://github.com/domn1995/dunet" /> for more information.</remarks>
[Dunet.Union]
public abstract partial record class Result<TSuccess, TFailure>
{
    /// <summary>Represents a successful <see cref="Result{TSuccess, TFailure}" />.</summary>
    /// <param name="Value">The value of type <typeparamref name="TSuccess"/>.</param>
    public partial record class Success(TSuccess Value);

    /// <summary>Represents a failed <see cref="Result{TSuccess, TFailure}" />.</summary>
    /// <param name="Value">The value of type <typeparamref name="TFailure"/>.</param>
    public partial record class Failure(TFailure Value);
}
