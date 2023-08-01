// Copyright (c) ZeroC, Inc.

namespace IceRpc.Json;

/// <summary>Represents a failure that occurs while dispatching a request.</summary>
public class DispatchException : Exception
{
    public DispatchException(StatusCode statusCode, string? message) 
        : base(
            $"The dispatch failed with status code {statusCode}" +
            (message is not null ? $" and message: {message}" : ""))
    {
    }
}
