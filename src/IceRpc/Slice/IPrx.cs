// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>The common interface of all Prx structs. It gives access to an untyped proxy object that can send requests
/// to a remote IceRPC service.</summary>
public interface IPrx
{
    /// <summary>Gets or initializes the encode feature, used to customize the encoding of payloads created from this
    /// Prx.</summary>
    ISliceEncodeFeature? EncodeFeature { get; init; }

    /// <summary>Gets or initializes the target proxy object.</summary>
    Proxy Proxy { get; init; }
}
