// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Represents a delegate that encodes into an Ice encoder.</summary>
/// <param name="encoder">The Ice encoder.</param>
public delegate void EncodeAction(ref IceEncoder encoder);

/// <summary>Represents a delegate that encodes a value with an Ice encoder.</summary>
/// <typeparam name="T">The type of the value to encode.</typeparam>
/// <param name="encoder">The Ice encoder.</param>
/// <param name="value">The value to encode with the encoder.</param>
public delegate void EncodeAction<in T>(ref IceEncoder encoder, T value);
