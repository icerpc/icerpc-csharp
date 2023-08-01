// Copyright (c) ZeroC, Inc.

namespace GreeterCore;

/// <summary>Represents a failure that occurs while dispatching a request.</summary>
public class DispatchException : Exception
{
    public DispatchException(IceRpc.StatusCode statusCode, string? message) 
        : base(
            $"The dispatch failed with status code {statusCode}" +
            (message is not null ? $" and message: {message}" : ""))
    {
    }
}
