// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>The common interface of all proxy structs.</summary>
public interface IPrx
{
    /// <summary>Gets or initializes the encode feature, used to customize the encoding of payloads created from this
    /// proxy.</summary>
    ISliceEncodeFeature? EncodeFeature { get; init; }

    /// <summary>Gets or initializes the invocation pipeline of this proxy.</summary>
    IInvoker Invoker { get; init; }

    /// <summary>Gets or initializes the proxy data.</summary>
    Proxy Proxy { get; init; }
}
