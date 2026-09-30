// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Holds the failure value of a <see cref="Result{TSuccess, TFailure}" /> whose type arguments the Slice
/// compiler wraps. See <see cref="Result{TSuccess, TFailure}" />.</summary>
/// <typeparam name="T">The type of the failure value.</typeparam>
/// <param name="Value">The failure value.</param>
public sealed record class Failure<T>(T Value);
