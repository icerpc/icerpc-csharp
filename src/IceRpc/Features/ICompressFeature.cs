// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Indicates whether or not the payload of an icerpc request or response should be compressed.</summary>
/// <remarks>An interceptor or middleware needs to perform this compression.</remarks>
public interface ICompressFeature
{
    /// <summary>Gets a value indicating whether or not to compress the payload of an outgoing request or response.
    /// </summary>
    /// <value><see langword="true" /> if the payload should be compressed; <see langword="false" /> if the payload
    /// should be kept as-is.</value>
    bool Value { get; }
}
