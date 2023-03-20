// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>A feature that specifies whether or not the payload of an icerpc request or response should be compressed.
/// </summary>
/// <remarks>An interceptor or middleware needs to perform this compression.</remarks>
public interface ICompressFeature
{
    /// <summary>Gets a value indicating whether or not to compress the payload.</summary>
    /// <value><see langword="true" /> if the payload should be compressed; <see langword="false" /> otherwise.</value>
    bool Value { get; }
}
