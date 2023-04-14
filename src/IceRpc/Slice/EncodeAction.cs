// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Represents a delegate that encodes into a Slice encoder.</summary>
/// <param name="encoder">The Slice encoder.</param>
public delegate void EncodeAction(ref SliceEncoder encoder);

/// <summary>Represents a delegate that encodes a value with a Slice encoder.</summary>
/// <typeparam name="T">The type of the value to encode.</typeparam>
/// <param name="encoder">The Slice encoder.</param>
/// <param name="value">The value to encode with the encoder.</param>
public delegate void EncodeAction<in T>(ref SliceEncoder encoder, T value);
