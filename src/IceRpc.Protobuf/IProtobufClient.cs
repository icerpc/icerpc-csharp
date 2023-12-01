// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Represents a local ambassador for a remote service.</summary>
public interface IProtobufClient
{
    /// <summary>Gets or initializes the encode options, used to customize the encoding of payloads created from this
    /// client.</summary>
    ProtobufEncodeOptions? EncodeOptions { get; init; }

    /// <summary>Gets or initializes the invocation pipeline of this client.</summary>
    IInvoker Invoker { get; init; }

    /// <summary>Gets or initializes the address of the remote service.</summary>
    ServiceAddress ServiceAddress { get; init; }
}
