// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>A discriminated union that represents either a success or a failure. It is typically used as the return
/// type of Slice operations.</summary>
/// <typeparam name="TSuccess">The success type.</typeparam>
/// <typeparam name="TFailure">The failure type.</typeparam>
/// <remarks>The Slice built-in generic type Result maps to this class in C#.</remarks>
[Dunet.Union]
public abstract partial record class Result<TSuccess, TFailure>
{
    /// <summary>Represents a successful <see cref="Result{TSuccess, TFailure}" />.</summary>
    public partial record class Success(TSuccess Value);

    /// <summary>Represents a failed <see cref="Result{TSuccess, TFailure}" />.</summary>
    public partial record class Failure(TFailure Value);
}
