// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Holds the success value of a <see cref="Result{TSuccess, TFailure}" /> whose type arguments the Slice
/// compiler wraps. See <see cref="Result{TSuccess, TFailure}" />.</summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <param name="Value">The success value.</param>
public sealed record class Success<T>(T Value);
