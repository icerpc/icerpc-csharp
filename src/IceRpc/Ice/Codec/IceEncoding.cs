// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Describes the versions of the Ice encoding supported by this implementation.</summary>
public enum IceEncoding : byte
{
    /// <summary>Ice encoding version 1. It's identical to the Ice encoding version 1.1.</summary>
    Ice1 = 1,

    /// <summary>Ice encoding version 2.</summary>
    Ice2 = 2,
}
