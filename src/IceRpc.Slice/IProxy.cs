// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Represents a local ambassador for a remote service.</summary>
public interface IProxy
{
    /// <summary>Gets or initializes the encode options, used to customize the encoding of payloads created from this
    /// proxy.</summary>
    SliceEncodeOptions? EncodeOptions { get; init; }

    /// <summary>Gets or initializes the invocation pipeline of this proxy.</summary>
    IInvoker Invoker { get; init; }

    /// <summary>Gets or initializes the address of the remote service.</summary>
    ServiceAddress ServiceAddress { get; init; }
}
