// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>A proxy is a local ambassador for a remote service.</summary>
public interface IPrx
{
    /// <summary>Gets or initializes the encode feature, used to customize the encoding of payloads created from this
    /// proxy.</summary>
    ISliceEncodeFeature? EncodeFeature { get; init; }

    /// <summary>Gets or initializes the invocation pipeline of this proxy.</summary>
    IInvoker Invoker { get; init; }

    /// <summary>Gets or initializes the address of the remote service.</summary>
    ServiceAddress ServiceAddress { get; init; }
}
