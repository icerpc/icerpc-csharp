// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Represents a middleware with a single injected service dependency in its DispatchAsync method.</summary>
/// <typeparam name="TDep">The type of the injected dependency.</typeparam>
public interface IMiddleware<TDep> where TDep : notnull
{
    /// <summary>Dispatches a request and returns a response.</summary>
    ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, TDep dep, CancellationToken cancel);
}

/// <summary>Represents a middleware with 2 injected service dependencies in its DispatchAsync method.</summary>
/// <typeparam name="TDep1">The type of the first injected dependency.</typeparam>
/// <typeparam name="TDep2">The type of the second injected dependency.</typeparam>
public interface IMiddleware<TDep1, TDep2>
    where TDep1 : notnull
    where TDep2 : notnull
{
    /// <summary>Dispatches a request and returns a response.</summary>
    ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        TDep1 dep1,
        TDep2 dep2,
        CancellationToken cancel);
}

/// <summary>Represents a middleware with 3 injected service dependencies in its DispatchAsync method.</summary>
/// <typeparam name="TDep1">The type of the first injected dependency.</typeparam>
/// <typeparam name="TDep2">The type of the second injected dependency.</typeparam>
/// <typeparam name="TDep3">The type of the third injected dependency.</typeparam>
public interface IMiddleware<TDep1, TDep2, TDep3>
    where TDep1 : notnull
    where TDep2 : notnull
    where TDep3 : notnull
{
    /// <summary>Dispatches a request and returns a response.</summary>
    ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        TDep1 dep1,
        TDep2 dep2,
        TDep3 dep3,
        CancellationToken cancel);
}
