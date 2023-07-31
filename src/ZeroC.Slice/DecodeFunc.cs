// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Represents a delegate that decodes a value from a Slice decoder.</summary>
/// <typeparam name="T">The type of the value to decode.</typeparam>
/// <param name="decoder">The Slice decoder.</param>
/// <returns>The value.</returns>
public delegate T DecodeFunc<T>(ref SliceDecoder decoder);
