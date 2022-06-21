// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>A delegate that decodes a value from an Slice decoder.</summary>
/// <typeparam name="T">The type of the value to decode.</typeparam>
/// <param name="decoder">The Slice decoder.</param>
/// <returns>The value.</returns>
public delegate T DecodeFunc<T>(ref SliceDecoder decoder);

/// <summary>A delegate that decodes a trait from a Slice decoder.</summary>
/// <typeparam name="T">The type of the trait to decode.</typeparam>
/// <param name="typeId">The type ID of the trait.</param>
/// <param name="decoder">The Slice decoder.</param>
/// <returns>The trait.</returns>
public delegate T DecodeTraitFunc<T>(string typeId, ref SliceDecoder decoder);
