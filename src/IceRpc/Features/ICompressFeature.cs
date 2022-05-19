// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>A feature that specifies whether or not the payload of an icerpc request or response must be compressed.
/// </summary>
/// <remarks>An interceptor or middleware outside the IceRpc core library needs to perform this compression.</remarks>
public interface ICompressFeature
{
    /// <summary>Indicates whether or not to compress the payload.</summary>
    bool Value { get; }
}
