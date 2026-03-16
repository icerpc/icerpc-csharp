// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Represents a delegate that decodes a value from a Ice decoder.</summary>
/// <typeparam name="T">The type of the value to decode.</typeparam>
/// <param name="decoder">The Ice decoder.</param>
/// <returns>The value.</returns>
public delegate T DecodeFunc<T>(ref IceDecoder decoder);
