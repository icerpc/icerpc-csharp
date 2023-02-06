// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>A delegate that decodes a value from a Slice decoder.</summary>
/// <typeparam name="T">The type of the value to decode.</typeparam>
/// <param name="decoder">The Slice decoder.</param>
/// <returns>The value.</returns>
public delegate T DecodeFunc<T>(ref SliceDecoder decoder);

/// <summary>A delegate that decodes a Slice exception from a Slice decoder.</summary>
/// <param name="decoder">The Slice decoder.</param>
/// <param name="message">The exception message.</param>
/// <returns>The decoded Slice exception.</returns>
public delegate SliceException DecodeExceptionFunc(ref SliceDecoder decoder, string? message);
