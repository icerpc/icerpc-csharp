// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Slice decoders use activators to create instances of Slice classes and exceptions from Slice type IDs.
/// </summary>
public interface IActivator
{
    /// <summary>Creates an instance of a Slice class based on a type ID.</summary>
    /// <param name="typeId">The Slice type ID.</param>
    /// <param name="decoder">The decoder.</param>
    /// <returns>A new instance of the class identified by <paramref name="typeId" />.</returns>
    object? CreateClassInstance(string typeId, ref SliceDecoder decoder);

    /// <summary>Creates an instance of a Slice exception based on a type ID.</summary>
    /// <param name="typeId">The Slice type ID.</param>
    /// <param name="decoder">The decoder.</param>
    /// <param name="message">The exception message.</param>
    /// <returns>A new instance of the class identified by <paramref name="typeId" />.</returns>
    object? CreateExceptionInstance(string typeId, ref SliceDecoder decoder, string? message);
}
