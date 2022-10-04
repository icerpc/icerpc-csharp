// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>Slice decoders use activators to create instances of Slice constructs from Slice type IDs.
/// </summary>
public interface IActivator
{
    /// <summary>Creates an instance of a Slice construct based on a type ID.</summary>
    /// <param name="typeId">The Slice type ID.</param>
    /// <param name="decoder">The decoder.</param>
    /// <returns>A new instance of the type identified by <paramref name="typeId" />. This instance may be fully
    /// decoded using decoder, or only partially decoded.</returns>
    object? CreateInstance(string typeId, ref SliceDecoder decoder);
}
