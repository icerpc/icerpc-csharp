// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>A union that represents either a success or a failure. It is typically used as the return type of Slice
/// operations.</summary>
/// <typeparam name="TSuccess">The success type.</typeparam>
/// <typeparam name="TFailure">The failure type.</typeparam>
/// <remarks>The Slice Result type (a built-in generic type) maps to this generic union in C#. Its cases are
/// <see cref="Success{T}" /> and <see cref="Failure{T}" />.</remarks>
public readonly union Result<TSuccess, TFailure>(Success<TSuccess>, Failure<TFailure>)
    : IEquatable<Result<TSuccess, TFailure>>
{
    /// <inheritdoc/>
    public bool Equals(Result<TSuccess, TFailure> other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Result<TSuccess, TFailure> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    public override string ToString() => Value?.ToString() ?? "";

    /// <summary>Checks if two results are equal.</summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><see langword="true" /> if the results are equal; otherwise, <see langword="false" />.</returns>
    public static bool operator ==(Result<TSuccess, TFailure> left, Result<TSuccess, TFailure> right) =>
        left.Equals(right);

    /// <summary>Checks if two results are not equal.</summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><see langword="true" /> if the results differ; otherwise, <see langword="false" />.</returns>
    public static bool operator !=(Result<TSuccess, TFailure> left, Result<TSuccess, TFailure> right) =>
        !left.Equals(right);
}

/// <summary>Provides factory methods that create the cases of <see cref="Result{TSuccess, TFailure}" />.</summary>
/// <remarks>These methods are useful when the compiler infers the type argument from the value, as in
/// <c>Result.FromSuccess("hello")</c>. Otherwise, construct the case directly, as in
/// <c>new Failure&lt;MyError&gt;(new MyError.NotFound())</c>.</remarks>
public static class Result
{
    /// <summary>Creates a success case.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="value">The success value.</param>
    /// <returns>A new <see cref="Success{T}" />, which converts implicitly to any
    /// <see cref="Result{TSuccess, TFailure}" /> whose success type is <typeparamref name="T" />.</returns>
    public static Success<T> FromSuccess<T>(T value) => new(value);

    /// <summary>Creates a failure case.</summary>
    /// <typeparam name="T">The type of the failure value.</typeparam>
    /// <param name="value">The failure value.</param>
    /// <returns>A new <see cref="Failure{T}" />, which converts implicitly to any
    /// <see cref="Result{TSuccess, TFailure}" /> whose failure type is <typeparamref name="T" />.</returns>
    public static Failure<T> FromFailure<T>(T value) => new(value);
}

/// <summary>The success case of a <see cref="Result{TSuccess, TFailure}" />.</summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <param name="Value">The success value.</param>
public sealed record class Success<T>(T Value);

/// <summary>The failure case of a <see cref="Result{TSuccess, TFailure}" />.</summary>
/// <typeparam name="T">The type of the failure value.</typeparam>
/// <param name="Value">The failure value.</param>
public sealed record class Failure<T>(T Value);
